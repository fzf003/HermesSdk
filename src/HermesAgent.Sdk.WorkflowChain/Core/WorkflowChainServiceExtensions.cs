using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// DI 注册扩展。
/// </summary>
public static class WorkflowChainServiceExtensions
{
    /// <summary>
    /// 注册工作流引擎及其所有步骤处理器。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置工作流步骤的委托</param>
    /// <param name="store">状态存储实现。InMemoryStateStore() = Phase 1 默认。</param>
    public static IServiceCollection AddWorkflowChain(
        this IServiceCollection services,
        Action<WorkflowChainBuilder> configure,
        IWorkflowStateStore? store = null)
    {
        var builder = new WorkflowChainBuilder(services);
        configure(builder);

        // 状态存储
        services.AddSingleton(store ?? builder.GetOrCreateStateStore());

        // 工作流注册表（如果未通过 builder 创建）
        if (!services.Any(s => s.ServiceType == typeof(WorkflowRegistry)))
        {
            services.AddSingleton(new WorkflowRegistry());
        }

        // WorkflowVersionManager - 版本管理
        services.AddSingleton<WorkflowVersionManager>(sp =>
            new WorkflowVersionManager(sp.GetRequiredService<WorkflowRegistry>()));

        // WorkflowImportExportManager - 导入导出
        services.AddSingleton<WorkflowImportExportManager>(sp =>
            new WorkflowImportExportManager(
                sp.GetRequiredService<WorkflowRegistry>()));

        // WorkflowHotReloadManager - 热加载（可选）
        services.AddSingleton<WorkflowHotReloadManager>(sp =>
            new WorkflowHotReloadManager(
                sp.GetRequiredService<WorkflowRegistry>(),
                sp.GetRequiredService<WorkflowImportExportManager>(),
                sp.GetService<ILogger<WorkflowHotReloadManager>>()));

        // YamlWorkflowParser - YAML 解析器
        services.AddSingleton<YamlWorkflowParser>();

        // 注意：VariableResolver 不应在 DI 中注册，因为它需要绑定到特定的 WorkflowContext 实例
        // 用户应在使用时手动创建：var resolver = new VariableResolver(workflowContext);

        // 引擎
        services.AddSingleton<WorkflowEngine>();

        services.AddHostedService<WorkflowEngineInitializationService>();

        var heartbeatThreshold = builder.GetHeartbeatThreshold();
        if (heartbeatThreshold > TimeSpan.Zero)
        {
            services.AddHostedService(sp =>
                new WorkflowHeartbeatService(
                    sp.GetRequiredService<WorkflowEngine>(),
                    sp.GetRequiredService<IWorkflowStateStore>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorkflowHeartbeatService>>(),
                    heartbeatThreshold));
        }

        return services;
    }
}

/// <summary>
/// 工作流构建器 — 注册步骤处理器。
/// </summary>
public class WorkflowChainBuilder
{
    private readonly IServiceCollection _services;
    private IWorkflowStateStore? _stateStore;
    private TimeSpan _heartbeatThreshold = TimeSpan.FromMinutes(5);
    private WorkflowRegistry? _workflowRegistry;

    public WorkflowChainBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>注册一个步骤处理器（实现类中可以通过 DI 注入依赖）</summary>
    public WorkflowChainBuilder AddStep<T>() where T : class, IStepHandler
    {
        _services.AddTransient<T>();
        _services.AddTransient<IStepHandler, T>();
        return this;
    }

    /// <summary>注册一个步骤处理器（直接传入实例，无依赖注入）</summary>
    public WorkflowChainBuilder AddStep(IStepHandler handler)
    {
        _services.AddSingleton(handler);
        return this;
    }

    /// <summary>配置 Redis 状态存储</summary>
    public WorkflowChainBuilder AddRedisStateStore(string connectionString, int dbIndex = 0)
    {
        _stateStore = new RedisStateStore(connectionString, dbIndex);
        return this;
    }

    /// <summary>配置 SQLite 状态存储</summary>
    public WorkflowChainBuilder AddSqliteStateStore(string connectionString)
    {
        _stateStore = new SqliteStateStore(connectionString);
        return this;
    }

    /// <summary>配置心跳超时阈值（默认 5 分钟）</summary>
    public WorkflowChainBuilder SetHeartbeatThreshold(TimeSpan threshold)
    {
        _heartbeatThreshold = threshold;
        return this;
    }

    /// <summary>
    /// 从YAML文件注册工作流定义（异步版本）。
    /// </summary>
    /// <param name="yamlFilePath">YAML文件路径</param>
    /// <param name="version">版本号(可选,从文件中读取)</param>
    /// <returns>构建器实例</returns>
    public async Task<WorkflowChainBuilder> RegisterFromYamlAsync(string yamlFilePath, string? version = null)
    {
        var parser = new YamlWorkflowParser();
        var definition = await parser.ParseFromFileAsync(yamlFilePath);

        if (version != null)
            definition.Version = version;

        GetOrCreateRegistry().Register(definition);

        return this;
    }

    /// <summary>
    /// 从YAML文件注册工作流定义。
    /// 使用 Task.Run 包装避免同步阻塞导致的死锁。
    /// </summary>
    /// <param name="yamlFilePath">YAML文件路径</param>
    /// <param name="version">版本号(可选,从文件中读取)</param>
    /// <returns>构建器实例</returns>
    public WorkflowChainBuilder RegisterFromYaml(string yamlFilePath, string? version = null)
    {
        var parser = new YamlWorkflowParser();
        var definition = Task.Run(() => parser.ParseFromFileAsync(yamlFilePath)).GetAwaiter().GetResult();

        if (version != null)
            definition.Version = version;

        GetOrCreateRegistry().Register(definition);

        return this;
    }

    /// <summary>
    /// 从目录批量注册YAML工作流定义。
    /// </summary>
    /// <param name="directory">YAML文件目录</param>
    /// <param name="searchPattern">文件搜索模式(默认*.yaml)</param>
    /// <returns>构建器实例</returns>
    public WorkflowChainBuilder RegisterFromYamlDirectory(string directory, string searchPattern = "*.yaml")
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"目录不存在: {directory}");

        var parser = new YamlWorkflowParser();
        var yamlFiles = Directory.GetFiles(directory, searchPattern);

        foreach (var file in yamlFiles)
        {
            try
            {
                var definition = parser.ParseFromFileAsync(file).GetAwaiter().GetResult();
                GetOrCreateRegistry().Register(definition);
            }
            catch (Exception ex)
            {
                // 记录错误但继续处理其他文件
                System.Diagnostics.Debug.WriteLine($"警告: 解析工作流文件失败 {file}: {ex.Message}");
            }
        }

        return this;
    }

    /// <summary>获取工作流注册表</summary>
    internal WorkflowRegistry GetOrCreateRegistry()
    {
        if (_workflowRegistry == null)
        {
            _workflowRegistry = new WorkflowRegistry();
            _services.AddSingleton(_workflowRegistry);
        }
        return _workflowRegistry;
    }

    /// <summary>获取心跳阈值（供 AddWorkflowChain 使用）</summary>
    internal TimeSpan GetHeartbeatThreshold() => _heartbeatThreshold;

    /// <summary>获取或创建状态存储（供 AddWorkflowChain 使用）</summary>
    internal IWorkflowStateStore GetOrCreateStateStore() => _stateStore ?? new InMemoryStateStore();
}

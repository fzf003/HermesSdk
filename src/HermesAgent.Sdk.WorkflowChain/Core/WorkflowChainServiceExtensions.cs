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

        // 工作流注册表
        WorkflowRegistry registry;
        if (!services.Any(s => s.ServiceType == typeof(WorkflowRegistry)))
        {
            registry = new WorkflowRegistry();
            services.AddSingleton(registry);
        }
        else
        {
            registry = (WorkflowRegistry)services
                .First(s => s.ServiceType == typeof(WorkflowRegistry))
                .ImplementationInstance!;
        }

        // 注册 Fluent API 定义的工作流到 Registry
        foreach (var def in builder.GetWorkflowDefinitions())
        {
            registry.Register(def);
        }

        // WorkflowVersionManager - 版本管理
        services.AddSingleton<WorkflowVersionManager>(sp =>
            new WorkflowVersionManager(sp.GetRequiredService<WorkflowRegistry>()));

        // WorkflowImportExportManager - 导入导出
        services.AddSingleton<WorkflowImportExportManager>(sp =>
            new WorkflowImportExportManager(
                sp.GetRequiredService<WorkflowRegistry>()));

        // Fluent API 默认步骤策略配置
        services.AddSingleton<IReadOnlyDictionary<Type, StepHandlerDefaults>>(builder.GetFluentDefaults());

        // IStepRuntimeConfigProvider - 步骤运行时配置源
        services.AddSingleton<IStepRuntimeConfigProvider>(sp =>
        {
            var fluentDefaults = sp.GetRequiredService<IReadOnlyDictionary<Type, StepHandlerDefaults>>();
            return new DefaultStepRuntimeConfigProvider(fluentDefaults);
        });

        // WorkflowHotReloadManager - 热加载（可选）
        services.AddSingleton<WorkflowHotReloadManager>(sp =>
            new WorkflowHotReloadManager(
                sp.GetRequiredService<WorkflowRegistry>(),
                sp.GetRequiredService<WorkflowImportExportManager>(),
                sp.GetRequiredService<WorkflowEngine>(),
                sp.GetService<ILogger<WorkflowHotReloadManager>>()));

        // YamlWorkflowParser - YAML 解析器
        services.AddSingleton<YamlWorkflowParser>();

        // 注意：VariableResolver 不应在 DI 中注册，因为它需要绑定到特定的 WorkflowContext 实例
        // 用户应在使用时手动创建：var resolver = new VariableResolver(workflowContext);

        // 引擎
        services.AddSingleton<WorkflowEngine>();

        services.AddHostedService<WorkflowEngineInitializationService>();

        // WorkflowBootstrapper - 将 Registry 定义同步到 Engine 运行时
        services.AddSingleton<IWorkflowBootstrapper, WorkflowBootstrapper>();

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

        // 注册引擎选项
        services.AddSingleton(builder.Options);

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
    private readonly Dictionary<Type, StepHandlerDefaults> _fluentDefaults = new();
    private readonly List<WorkflowDefinition> _workflowDefinitions = new();
    private readonly HashSet<string> _handlerStepIds = new();

    /// <summary>DI 服务集合（供 DslStepBuilder 等内部组件使用）</summary>
    internal IServiceCollection Services => _services;

    /// <summary>工作流引擎选项，控制 YAML 配置目录和自动导出等行为。</summary>
    public WorkflowEngineOptions Options { get; } = new();

    public WorkflowChainBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>注册一个步骤处理器（实现类中可以通过 DI 注入依赖）</summary>
    public WorkflowChainBuilder AddStep<T>() where T : class, IStepHandler
    {
        _services.AddTransient<T>();
        _services.AddTransient<IStepHandler, T>();
        TryTrackStepId<T>();
        return this;
    }

    /// <summary>注册一个步骤处理器（直接传入实例，无依赖注入）</summary>
    public WorkflowChainBuilder AddStep(IStepHandler handler)
    {
        _services.AddSingleton(handler);
        _handlerStepIds.Add(handler.StepId);
        return this;
    }

    /// <summary>
    /// 注册 CodeStepHandler 并可选地通过 Fluent API 配置默认步骤策略。
    /// 优先级：YAML &gt; Fluent 配置 &gt; Handler 虚属性 &gt; 引擎内建。
    /// </summary>
    internal WorkflowChainBuilder AddCodeStep<T>(Action<CodeStepBuilder<T>>? configure = null)
        where T : CodeStepHandler
    {
        _services.AddTransient<T>();
        _services.AddTransient<IStepHandler, T>();

        if (configure != null)
        {
            var stepBuilder = new CodeStepBuilder<T>();
            configure(stepBuilder);
            _fluentDefaults[typeof(T)] = stepBuilder.Build();
        }

        TryTrackStepId<T>();
        return this;
    }

    /// <summary>
    /// 注册 AgentStepHandler 并可选地通过 Fluent API 配置默认步骤策略和 prompt 模板。
    /// 优先级：YAML &gt; Fluent 配置 &gt; Handler 虚属性 &gt; 引擎内建。
    /// </summary>
    internal WorkflowChainBuilder AddAgentStep<T>(Action<AgentStepBuilder<T>>? configure = null)
        where T : AgentStepHandler
    {
        _services.AddTransient<T>();
        _services.AddTransient<IStepHandler, T>();

        if (configure != null)
        {
            var stepBuilder = new AgentStepBuilder<T>();
            configure(stepBuilder);
            _fluentDefaults[typeof(T)] = stepBuilder.Build();
        }

        TryTrackStepId<T>();
        return this;
    }

    /// <summary>
    /// 注册 HumanApprovalStepHandler 并可选地通过 Fluent API 配置默认步骤策略。
    /// 优先级：YAML &gt; Fluent 配置 &gt; Handler 虚属性 &gt; 引擎内建。
    /// </summary>
    internal WorkflowChainBuilder AddHumanApprovalStep<T>(Action<HumanApprovalStepBuilder<T>>? configure = null)
        where T : HumanApprovalStepHandler
    {
        _services.AddTransient<T>();
        _services.AddTransient<IStepHandler, T>();

        if (configure != null)
        {
            var stepBuilder = new HumanApprovalStepBuilder<T>();
            configure(stepBuilder);
            _fluentDefaults[typeof(T)] = stepBuilder.Build();
        }

        TryTrackStepId<T>();
        return this;
    }

    /// <summary>
    /// 按工作流分组注册步骤。
    /// callback 内通过 WorkflowStepBuilder 链式注册步骤，
    /// 返回值通过 WorkflowDefinitionBuilder 设置工作流元数据。
    /// </summary>
    public WorkflowDefinitionBuilder AddWorkflow(Action<WorkflowStepBuilder> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var wfBuilder = new WorkflowStepBuilder(this);
        configure(wfBuilder);

        // 步骤资格校验 — 在注册时尽可能验证 StepId 等元数据
        var (stepDefinitions, errors) = BuildStepDefinitions(wfBuilder.Steps, _fluentDefaults, _services);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"AddWorkflow 步骤资格校验失败:\n" +
                string.Join("\n", errors.Select(e => $"  - {e}")));

        return new WorkflowDefinitionBuilder(this, stepDefinitions);
    }

    /// <summary>
    /// 按工作流分组注册步骤（指定工作流名称）。
    /// 等效于 <c>AddWorkflow(configure).WithName(name)</c>。
    /// </summary>
    public WorkflowDefinitionBuilder AddWorkflow(string name, Action<WorkflowStepBuilder> configure)
    {
        return AddWorkflow(configure).WithName(name);
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

    /// <summary>获取 Fluent API 默认步骤策略配置（供 AddWorkflowChain 使用）</summary>
    internal IReadOnlyDictionary<Type, StepHandlerDefaults> GetFluentDefaults() => _fluentDefaults;

    /// <summary>获取 Fluent API 定义的工作流列表（供 AddWorkflowChain 使用）</summary>
    internal IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions() => _workflowDefinitions;

    /// <summary>注册工作流定义到内部列表（供 WorkflowDefinitionBuilder 调用）</summary>
    internal void AddWorkflowDefinition(WorkflowDefinition definition)
    {
        _workflowDefinitions.Add(definition);
    }

    /// <summary>检查工作流名称是否已注册（供 WorkflowDefinitionBuilder 调用）</summary>
    internal bool HasWorkflowDefinition(string name)
    {
        return _workflowDefinitions.Any(d =>
            string.Equals(d.Name, name, StringComparison.Ordinal));
    }

    /// <summary>获取已注册的 Handler 步骤 ID 集合（供 YAML 加载时 handler 存在性校验使用）</summary>
    internal IReadOnlySet<string> GetHandlerStepIds() => _handlerStepIds;

    /// <summary>通过 DI 容器获取 Handler 步骤 ID 并加入跟踪集合</summary>
    private void TryTrackStepId<T>() where T : IStepHandler
    {
        try
        {
            var stepId = ResolveStepId(typeof(T), _services);
            if (!string.IsNullOrWhiteSpace(stepId))
                _handlerStepIds.Add(stepId);
        }
        catch
        {
            // 忽略 — 依赖尚未就绪时跳过跟踪，不影响后续运行
        }
    }

    /// <summary>从 DI 容器或通过无参构造函数解析 Handler 的 StepId</summary>
    private static string ResolveStepId(Type handlerType, IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        var resolved = (IStepHandler)sp.GetRequiredService(handlerType);
        return resolved.StepId;
    }

    internal static (List<StepDefinition> Steps, List<string> Errors) BuildStepDefinitions(
        IReadOnlyList<StepInfo> steps,
        IReadOnlyDictionary<Type, StepHandlerDefaults> fluentDefaults,
        IServiceCollection services)
    {
        var stepDefinitions = new List<StepDefinition>();
        var errors = new List<string>();

        foreach (var step in steps)
        {
            try
            {
                var stepId = ReadStepId(step.HandlerType, services);
                if (string.IsNullOrWhiteSpace(stepId))
                {
                    errors.Add($"步骤 {step.HandlerType.Name}.StepId 返回空值");
                    continue;
                }

                // 合并 Fluent API 配置值到 StepDefinition，使 ExportToYaml 可导出
                fluentDefaults.TryGetValue(step.HandlerType, out var fluent);

                stepDefinitions.Add(new StepDefinition
                {
                    Id = stepId,
                    Type = step.StepType,
                    Class = step.HandlerType.FullName ?? step.HandlerType.Name,
                    Assembly = step.HandlerType.Assembly.GetName().Name,
                    Timeout = fluent?.Timeout,
                    TimeoutAction = fluent?.TimeoutAction,
                    Retry = fluent?.Retry,
                    ErrorPolicy = fluent?.ErrorPolicy,
                    Prompt = fluent?.Prompt,
                    SystemPrompt = fluent?.SystemPrompt,
                    RouteName = fluent?.RouteName,
                    EventType = fluent?.EventType,
                    Notification = fluent?.Notification,
                    HeartbeatExtension = fluent?.HeartbeatExtension,
                });
            }
            catch (Exception ex)
            {
                errors.Add($"步骤 {step.HandlerType.Name} 初始化失败: {ex.Message}");
            }
        }

        return (stepDefinitions, errors);
    }

    private static string ReadStepId(Type handlerType, IServiceCollection services)
    {
        return ResolveStepId(handlerType, services);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    /// <summary>获取心跳阈值（供 AddWorkflowChain 使用）</summary>
    internal TimeSpan GetHeartbeatThreshold() => _heartbeatThreshold;

    /// <summary>获取或创建状态存储（供 AddWorkflowChain 使用）</summary>
    internal IWorkflowStateStore GetOrCreateStateStore() => _stateStore ?? new InMemoryStateStore();
}

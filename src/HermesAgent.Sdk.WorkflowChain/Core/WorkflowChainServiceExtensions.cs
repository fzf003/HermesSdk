using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton(store ?? new InMemoryStateStore());

        // 引擎
        services.AddSingleton<WorkflowEngine>();

        return services;
    }
}

/// <summary>
/// 工作流构建器 — 注册步骤处理器。
/// </summary>
public class WorkflowChainBuilder
{
    private readonly IServiceCollection _services;

    public WorkflowChainBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>注册一个步骤处理器（实现类中可通过 DI 注入依赖）</summary>
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
        _services.AddTransient<IStepHandler>(_ => handler);
        return this;
    }
}

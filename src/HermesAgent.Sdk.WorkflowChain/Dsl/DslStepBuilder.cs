namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// DSL 步骤构建器实现 — 实现 <see cref="IStepBuilder"/>，
/// 在 <see cref="Workflow.Build"/> 期间收集步骤信息，
/// 最终通过 <see cref="BuildDefinition"/> 产出 <see cref="WorkflowDefinition"/>。
/// </summary>
internal sealed class DslStepBuilder : IStepBuilder
{
    private readonly WorkflowChainBuilder _parent;

    /// <summary>
    /// 步骤来源 — 匿名 Lambda 或已存在的 Handler 类。
    /// 匿名步骤在 BuildDefinition 时实例化并注册到 DI；
    /// Handler 类步骤已通过父 Builder 注册，仅记录定义信息。
    /// </summary>
    private abstract record StepEntry(string StepId, StepType Type, DslCodeStepBuilder Builder);
    private sealed record AnonymousEntry(
        string StepId,
        StepType Type,
        DslCodeStepBuilder Builder,
        Func<StepHandlerDefaults?, IStepHandler> HandlerFactory) : StepEntry(StepId, Type, Builder);
    private sealed record HandlerClassEntry(
        string StepId,
        StepType Type,
        DslCodeStepBuilder Builder,
        Type HandlerType) : StepEntry(StepId, Type, Builder);

    private readonly List<StepEntry> _steps = new();

    internal DslStepBuilder(WorkflowChainBuilder parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    // ── IStepBuilder ──

    public DslCodeStepBuilder AddCodeStep(
        string stepId,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("步骤 ID 不能为空", nameof(stepId));

        var builder = new DslCodeStepBuilder();
        _steps.Add(new AnonymousEntry(
            stepId, StepType.Code, builder,
            defaults => new AnonymousCodeStepHandler(stepId, execute, defaults)));
        return builder;
    }

    public DslAgentStepBuilder AddAgentStep(
        string stepId,
        Func<WorkflowContext, AgentConfig>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("步骤 ID 不能为空", nameof(stepId));

        var builder = new DslAgentStepBuilder();
        _steps.Add(new AnonymousEntry(
            stepId, StepType.Agent, builder,
            defaults => new AnonymousAgentStepHandler(stepId, configure, defaults)));
        return builder;
    }

    public void AddCodeStep<T>() where T : CodeStepHandler
    {
        // 委托父 Builder 完成 DI 注册（transient，支持构造函数注入）
        _parent.AddCodeStep<T>();

        var handler = Activator.CreateInstance<T>();
        _steps.Add(new HandlerClassEntry(
            handler.StepId, StepType.Code, new DslCodeStepBuilder(), typeof(T)));
    }

    public void AddAgentStep<T>() where T : AgentStepHandler
    {
        _parent.AddAgentStep<T>();

        var handler = Activator.CreateInstance<T>();
        _steps.Add(new HandlerClassEntry(
            handler.StepId, StepType.Agent, new DslAgentStepBuilder(), typeof(T)));
    }

    // ── Build ──

    internal WorkflowDefinition BuildDefinition(string id, string name, string version, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("工作流名称不能为空", nameof(name));

        var stepDefs = new List<StepDefinition>();
        var seenStepIds = new HashSet<string>();

        foreach (var entry in _steps)
        {
            if (!seenStepIds.Add(entry.StepId))
                throw new InvalidOperationException(
                    $"工作流 \"{name}\" 中检测到重复的步骤 ID: \"{entry.StepId}\"");

            switch (entry)
            {
                case AnonymousEntry anonymous:
                    BuildAnonymousStep(anonymous, stepDefs);
                    break;
                case HandlerClassEntry handlerClass:
                    BuildHandlerClassStep(handlerClass, stepDefs);
                    break;
            }
        }

        if (stepDefs.Count == 0)
            throw new InvalidOperationException($"工作流 \"{name}\" 必须包含至少一个步骤");

        var definition = new WorkflowDefinition
        {
            Id = id,
            Name = name,
            Version = version,
            Description = description,
            Steps = stepDefs,
        };

        _parent.AddWorkflowDefinition(definition);
        return definition;
    }

    private void BuildAnonymousStep(AnonymousEntry entry, List<StepDefinition> stepDefs)
    {
        var defaults = entry.Builder.BuildDefaults();
        var handler = entry.HandlerFactory(defaults);

        // 注册到 DI（单例实例）
        _parent.AddStep(handler);

        stepDefs.Add(new StepDefinition
        {
            Id = handler.StepId,
            Type = entry.Type,
            Class = handler.GetType().FullName ?? handler.GetType().Name,
            Assembly = handler.GetType().Assembly.GetName().Name,
            // 写入 Fluent 配置值到 StepDefinition，使 ExportToYaml 可导出
            // 运行时优先级不变：YAML > Fluent（handler 虚属性）> 引擎内建
            Timeout = defaults.Timeout,
            TimeoutAction = defaults.TimeoutAction,
            Retry = defaults.Retry,
            ErrorPolicy = defaults.ErrorPolicy,
            Prompt = defaults.Prompt,
            SystemPrompt = defaults.SystemPrompt,
            RouteName = defaults.RouteName,
            EventType = defaults.EventType,
            HeartbeatExtension = defaults.HeartbeatExtension,
        });
    }

    private void BuildHandlerClassStep(HandlerClassEntry entry, List<StepDefinition> stepDefs)
    {
        // Handler 已通过父 Builder 注册到 DI，不再重复注册
        stepDefs.Add(new StepDefinition
        {
            Id = entry.StepId,
            Type = entry.Type,
            Class = entry.HandlerType.FullName ?? entry.HandlerType.Name,
            Assembly = entry.HandlerType.Assembly.GetName().Name,
        });
    }
}

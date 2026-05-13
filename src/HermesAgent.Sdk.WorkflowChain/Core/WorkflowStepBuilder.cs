namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤注册信息 —— WorkflowStepBuilder 内部记录，用于构建 WorkflowDefinition。
/// </summary>
internal sealed record StepInfo(Type HandlerType, StepType StepType);

/// <summary>
/// 工作流步骤构建器 — 支持在 AddWorkflow 内链式注册步骤。
/// 内部收集步骤注册信息，供 WorkflowChainBuilder 构建 WorkflowDefinition。
/// </summary>
public sealed class WorkflowStepBuilder
{
    private readonly WorkflowChainBuilder _parent;
    private readonly List<StepInfo> _steps = new();

    internal IReadOnlyList<StepInfo> Steps => _steps;

    internal WorkflowStepBuilder(WorkflowChainBuilder parent) => _parent = parent;

    public WorkflowStepBuilder AddCodeStep<T>(Action<CodeStepBuilder<T>>? configure = null)
        where T : CodeStepHandler
    {
        _parent.AddCodeStep(configure);
        _steps.Add(new StepInfo(typeof(T), StepType.Code));
        return this;
    }

    public WorkflowStepBuilder AddAgentStep<T>(Action<AgentStepBuilder<T>>? configure = null)
        where T : AgentStepHandler
    {
        _parent.AddAgentStep(configure);
        _steps.Add(new StepInfo(typeof(T), StepType.Agent));
        return this;
    }

    public WorkflowStepBuilder AddHumanApprovalStep<T>(Action<HumanApprovalStepBuilder<T>>? configure = null)
        where T : HumanApprovalStepHandler
    {
        _parent.AddHumanApprovalStep(configure);
        _steps.Add(new StepInfo(typeof(T), StepType.HumanApproval));
        return this;
    }
}

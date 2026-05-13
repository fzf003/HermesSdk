namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流定义构建器 — AddWorkflow 的返回值，用于设置工作流元数据。
/// WithName 创建 WorkflowDefinition 并注册到 WorkflowChainBuilder，
/// WithVersion/WithDescription 修改已有 WorkflowDefinition 属性。
/// </summary>
public sealed class WorkflowDefinitionBuilder
{
    private readonly WorkflowChainBuilder _parent;
    private readonly List<StepDefinition> _stepDefinitions;
    private WorkflowDefinition? _definition;

    internal WorkflowDefinitionBuilder(
        WorkflowChainBuilder parent,
        List<StepDefinition> stepDefinitions)
    {
        _parent = parent;
        _stepDefinitions = stepDefinitions;
    }

    /// <summary>设置工作流名称并创建 WorkflowDefinition（必须先调用）。</summary>
    public WorkflowDefinitionBuilder WithName(string name)
    {
        if (_definition != null)
            throw new InvalidOperationException(
                $"工作流 \"{name}\" 的 WithName 只能调用一次");

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("工作流名称不能为空", nameof(name));

        if (_parent.HasWorkflowDefinition(name))
            throw new InvalidOperationException(
                $"工作流 \"{name}\" 已通过 AddWorkflow 注册，不允许重复注册");

        _definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Version = "1.0",
            Steps = _stepDefinitions,
        };

        _parent.AddWorkflowDefinition(_definition);
        return this;
    }

    /// <summary>设置工作流版本（可选，默认 "1.0"）。</summary>
    public WorkflowDefinitionBuilder WithVersion(string version)
    {
        EnsureDefinition();
        _definition!.Version = version ?? throw new ArgumentNullException(nameof(version));
        return this;
    }

    /// <summary>设置工作流描述（可选）。</summary>
    public WorkflowDefinitionBuilder WithDescription(string? description)
    {
        EnsureDefinition();
        _definition!.Description = description;
        return this;
    }

    private void EnsureDefinition()
    {
        if (_definition == null)
            throw new InvalidOperationException("必须先调用 WithName 设置工作流名称");
    }
}

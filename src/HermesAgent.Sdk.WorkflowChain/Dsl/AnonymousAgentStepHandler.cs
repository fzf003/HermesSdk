namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// 匿名 Agent 步骤 Handler — 将 DSL 内联 Lambda 包装为 <see cref="AgentStepHandler"/>。
/// Fluent 配置值直接注入虚属性，参与三优先级合并。
/// </summary>
internal sealed class AnonymousAgentStepHandler : AgentStepHandler
{
    public override string StepId { get; }
    public override string? Timeout { get; }
    public override string? TimeoutAction { get; }
    public override RetryConfigYaml? Retry { get; }
    public override string? ErrorPolicy { get; }
    public override string? Prompt { get; }
    public override string? SystemPrompt { get; }
    public override string RouteName { get; }
    public override string EventType { get; }

    private readonly Func<WorkflowContext, AgentConfig>? _configFactory;

    public AnonymousAgentStepHandler(
        string stepId,
        Func<WorkflowContext, AgentConfig>? configFactory,
        StepHandlerDefaults? defaults = null)
    {
        StepId = stepId ?? throw new ArgumentNullException(nameof(stepId));
        _configFactory = configFactory;
        Timeout = defaults?.Timeout;
        TimeoutAction = defaults?.TimeoutAction;
        Retry = defaults?.Retry;
        ErrorPolicy = defaults?.ErrorPolicy;
        Prompt = defaults?.Prompt;
        SystemPrompt = defaults?.SystemPrompt;
        RouteName = defaults?.RouteName ?? "";
        EventType = defaults?.EventType ?? "";
    }

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        // Agent 步骤完成后默认 Complete，输出保留在 context.StepOutputs 中
        // 上游 CodeStep 可通过 GetOutput 读取并决定下一步流向
        return Task.FromResult(Complete(context.StepOutputs.TryGetValue(StepId, out var output) ? output : null));
    }

    public override string BuildPrompt(WorkflowContext context)
    {
        if (_configFactory != null)
        {
            var config = _configFactory(context);
            return config.UserPrompt ?? Prompt ?? "";
        }
        return Prompt ?? "";
    }
}

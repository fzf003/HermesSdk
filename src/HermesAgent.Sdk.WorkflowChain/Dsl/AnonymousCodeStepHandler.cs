namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// 匿名 Code 步骤 Handler — 将 DSL 内联 Lambda 包装为 <see cref="CodeStepHandler"/>。
/// Fluent 配置值直接注入虚属性，参与 <c>MergeStepRuntimeConfig</c> 的三优先级合并，
/// 无需通过 <c>_fluentDefaults</c> 类型索引。
/// </summary>
internal sealed class AnonymousCodeStepHandler : CodeStepHandler
{
    public override string StepId { get; }
    public override string? Timeout { get; }
    public override string? TimeoutAction { get; }
    public override RetryConfigYaml? Retry { get; }
    public override string? ErrorPolicy { get; }

    private readonly Func<WorkflowContext, CancellationToken, Task<StepResult>> _execute;

    public AnonymousCodeStepHandler(
        string stepId,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute,
        StepHandlerDefaults? defaults = null)
    {
        StepId = stepId ?? throw new ArgumentNullException(nameof(stepId));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Timeout = defaults?.Timeout;
        TimeoutAction = defaults?.TimeoutAction;
        Retry = defaults?.Retry;
        ErrorPolicy = defaults?.ErrorPolicy;
    }

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        => _execute(context, ct);
}

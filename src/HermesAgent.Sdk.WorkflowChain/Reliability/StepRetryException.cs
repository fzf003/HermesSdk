namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤重试耗尽异常 — 用于在 <see cref="RetryExecutor"/> 中触发重试循环，
/// 重试次数耗尽后抛出，由 <see cref="WorkflowEngine"/> 捕获并转为 <see cref="StepResult"/>。
/// </summary>
internal sealed class StepRetryException : Exception
{
    public StepRetryException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

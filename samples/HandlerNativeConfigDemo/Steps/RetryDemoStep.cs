namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>重试代码步骤 — 配置由 Fluent API 外部化</summary>
internal sealed class RetryDemoStep : CodeStepHandler
{
    public override string StepId => "retry-demo";
    public static int ExecutionCount;

    public static void ResetCount() => ExecutionCount = 0;

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        ExecutionCount++;
        Console.WriteLine($"  🔄 RetryDemoStep: attempt #{ExecutionCount}");
        await Task.Delay(10, ct);
        throw new TimeoutException("Always failing for retry demo");
    }
}

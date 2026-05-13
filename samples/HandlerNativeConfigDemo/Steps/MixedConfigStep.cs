namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>混合策略步骤 — 配置由 Fluent API 外部化</summary>
internal sealed class MixedConfigStep : CodeStepHandler
{
    public override string StepId => "mixed-step";
    public static int ExecutionCount;

    public static void ResetCount() => ExecutionCount = 0;

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        ExecutionCount++;
        Console.WriteLine($"  🔄 MixedConfigStep: attempt #{ExecutionCount}");
        await Task.Delay(2000, ct);
        Console.WriteLine("  ⚠ MixedConfigStep: 2s 等待完成 (说明 timeout 足够长)");
        if (ExecutionCount < 2) throw new TimeoutException("Failing for retry demo");
        return Complete(new { done = true });
    }
}

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>2s 超时代码步骤 — 配置由 Fluent API 外部化</summary>
internal sealed class TimeoutDemoStep : CodeStepHandler
{
    public override string StepId => "timeout-demo";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  ⏳ TimeoutDemoStep: 开始 5s 等待...");
        await Task.Delay(5000, ct);
        return Complete(new { done = true });
    }
}

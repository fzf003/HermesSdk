namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>无默认配置的 Handler — 向后兼容</summary>
internal sealed class NoDefaultStep : CodeStepHandler
{
    public override string StepId => "no-default-step";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  ℹ️ NoDefaultStep: 执行完成 (使用引擎默认值)");
        return Complete(new { ok = true });
    }
}

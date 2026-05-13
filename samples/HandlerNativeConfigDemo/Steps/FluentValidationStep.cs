namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>Fluent 配置演示步骤 — 验证 Fluent ErrorPolicy</summary>
internal sealed class FluentValidationStep : CodeStepHandler
{
    public override string StepId => "fluent-validation";
    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  ✅ FluentValidationStep: 执行完成 (Fluent Timeout=15s 生效)");
        return Task.FromResult(Sequential("fluent-agent"));
    }
}

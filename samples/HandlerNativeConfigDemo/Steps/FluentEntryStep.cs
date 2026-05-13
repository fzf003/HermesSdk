namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>入口步骤</summary>
internal sealed class FluentEntryStep : CodeStepHandler
{
    public override string StepId => "fluent-entry";
    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  🚀 FluentEntryStep: 开始工作流 → fluent-process");
        return Task.FromResult(Sequential("fluent-process"));
    }
}

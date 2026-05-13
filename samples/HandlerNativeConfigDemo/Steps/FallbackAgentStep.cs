namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>既无 Handler Prompt 也无 YAML Prompt — 回退到 BuildPrompt</summary>
internal sealed class FallbackAgentStep : AgentStepHandler
{
    public override string StepId => "fallback-agent";
    public override string RouteName => "demo.route";
    public override string EventType => "demo.event";

    public override string? ErrorPolicy => base.ErrorPolicy;

    public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
    public override string? Prompt => null;
    public override string BuildPrompt(WorkflowContext context) => "来自 BuildPrompt 的回退";
    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        => Task.FromResult(Complete(new { ok = true }));
}

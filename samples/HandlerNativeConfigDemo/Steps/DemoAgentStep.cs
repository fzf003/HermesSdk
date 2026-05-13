namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>AgentStepHandler — 演示 Prompt 默认值提取</summary>
internal sealed class DemoAgentStep : AgentStepHandler
{
    public override string StepId => "agent-demo";
    public override string RouteName => "demo.route";
    public override string EventType => "demo.event";
    public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
    public override string BuildPrompt(WorkflowContext context) => "回退 Prompt";
    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        => Task.FromResult(Complete(new { ok = true }));
}

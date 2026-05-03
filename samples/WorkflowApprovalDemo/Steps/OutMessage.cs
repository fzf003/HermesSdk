namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

public record OutMessage
{
    public string Message { get; init; } = "";
    public bool Success { get; init; }
}

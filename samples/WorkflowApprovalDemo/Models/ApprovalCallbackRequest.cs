namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;

/// <summary>人工审批回调请求</summary>
public class ApprovalCallbackRequest
{
    /// <summary>步骤 ID</summary>
    public string StepId { get; set; } = "";

    /// <summary>审批决策：approved 或 rejected</summary>
    public string Decision { get; set; } = "";

    /// <summary>审批意见</summary>
    public string? Comment { get; set; }

    /// <summary>审批人标识</summary>
    public string? ApproverId { get; set; }
}

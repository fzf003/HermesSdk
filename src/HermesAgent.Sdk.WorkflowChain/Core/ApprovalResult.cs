namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 人工审批结果 — OnHumanApprovalCallbackAsync 回调的结构化决策。
/// </summary>
public record ApprovalResult
{
    /// <summary>审批决策："approved" 或 "rejected"</summary>
    public string Decision { get; init; } = "";

    /// <summary>审批意见</summary>
    public string? Comment { get; init; }

    /// <summary>审批人标识</summary>
    public string? ApproverId { get; init; }
}

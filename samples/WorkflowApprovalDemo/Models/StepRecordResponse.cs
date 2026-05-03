namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;

/// <summary>步骤执行记录响应</summary>
public class StepRecordResponse
{
    /// <summary>步骤 ID</summary>
    public string StepId { get; set; } = "";

    /// <summary>步骤类型</summary>
    public string StepType { get; set; } = "";

    /// <summary>步骤状态</summary>
    public string Status { get; set; } = "";

    /// <summary>开始时间</summary>
    public string? StartedAt { get; set; }

    /// <summary>执行耗时（毫秒）</summary>
    public double? Duration { get; set; }

    /// <summary>输出快照</summary>
    public string? OutputSnapshot { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
}

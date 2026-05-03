namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;

/// <summary>工作流实例状态响应</summary>
public class WorkflowInstanceResponse
{
    /// <summary>实例 ID</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>实例状态</summary>
    public string Status { get; set; } = "";

    /// <summary>入口步骤 ID</summary>
    public string? EntryStepId { get; set; }

    /// <summary>是否运行中</summary>
    public bool IsRunning { get; set; }

    /// <summary>活跃步骤 ID 列表</summary>
    public List<string> ActiveSteps { get; set; } = new();
}

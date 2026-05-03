namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;

/// <summary>启动工作流请求</summary>
public class StartWorkflowRequest
{
    /// <summary>入口步骤 ID</summary>
    public string EntryStepId { get; set; } = "";

    /// <summary>实例 ID（可选，不填则自动生成）</summary>
    public string? InstanceId { get; set; }

    /// <summary>初始输入数据</summary>
    public Dictionary<string, object?>? InitialInput { get; set; }
}

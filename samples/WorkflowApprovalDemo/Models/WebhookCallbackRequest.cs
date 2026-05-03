namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;

/// <summary>Agent Webhook 回调请求</summary>
public class WebhookCallbackRequest
{
    /// <summary>已完成步骤 ID</summary>
    public string StepId { get; set; } = "";

    /// <summary>步骤输出</summary>
    public string? Output { get; set; }

    /// <summary>错误信息</summary>
    public string? Error { get; set; }
}

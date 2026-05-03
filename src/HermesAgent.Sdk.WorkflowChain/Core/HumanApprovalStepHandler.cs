namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 人工审批步骤处理器 — 引擎不知道如何通知审批人（邮件/IM/推送），
/// 因此由 Handler 在 DispatchAsync 中自行发送审批通知。
/// ExecuteAsync 在审批回调到达后调用，决定分支路由。
/// </summary>
public abstract class HumanApprovalStepHandler : StepHandlerBase
{
    /// <summary>
    /// 人工审批步骤默认心跳扩展 24 小时，覆盖全局心跳阈值。
    /// 子类可重写此属性以调整扩展时长。
    /// </summary>
    public override TimeSpan? HeartbeatExtension => TimeSpan.FromHours(24);

    /// <summary>
    /// 分发阶段：Handler 在此发送审批通知（邮件/IM/推送等）。
    /// 引擎不使用返回值，步骤保持 Dispatched 状态等待回调。
    /// </summary>
    public abstract Task DispatchAsync(WorkflowContext context, CancellationToken ct);

    /// <summary>
    /// 回调阶段：审批结果到达后，Handler 决定分支路由。
    /// 与其他 Handler 的 ExecuteAsync 语义一致——只调用一次。
    /// </summary>
    public override abstract Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct);

    /// <summary>
    /// 构建审批请求内容文本。供 DispatchAsync 中使用。
    /// 默认返回空字符串，子类可按需重写。
    /// </summary>
    public virtual string BuildApprovalMessage(WorkflowContext context) => "";
}

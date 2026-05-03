namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

/// <summary>经理审批步骤 — 人工审批</summary>
public class ManagerApprovalStep : HumanApprovalStepHandler
{
    public override string StepId => "manager-approval";

    public override string BuildApprovalMessage(WorkflowContext context)
    {
        var title = context.InitialInput["title"];
        var amount = context.InitialInput["amount"];
        var requester = context.InitialInput["requester"];
        return $"审批请求: {requester} 提交的 {title}，金额 {amount} 元，请审批";
    }

    public override Task DispatchAsync(WorkflowContext context, CancellationToken ct)
    {
        // 模拟发送审批通知（实际项目中可注入邮件/IM 服务）
        var message = BuildApprovalMessage(context);
        Console.WriteLine($"  [审批通知] 📧 {message}");
        return Task.CompletedTask;
    }

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var approval = context.StepOutputs[StepId] as ApprovalResult;
        if (approval == null)
            return Task.FromResult(Failed(new Exception("无审批结果")));

        Console.WriteLine(
            $"  [ManagerApproval] 决策: {approval.Decision}, 意见: {approval.Comment}, 审批人: {approval.ApproverId}"
        );

        // 审批通过 → 通知步骤；审批拒绝 → 升级步骤
        if (approval.Decision == "approved")
            return Task.FromResult(Sequential("notify-step"));
        else
            return Task.FromResult(Sequential("escalation-step"));
    }
}

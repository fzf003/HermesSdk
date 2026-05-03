namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

/// <summary>升级步骤 — 审批拒绝后升级处理</summary>
public class EscalationAgentStep : CodeStepHandler
{
    public override string StepId => "escalation-step";

    public override async Task<StepResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken ct
    )
    {
        var title = context.InitialInput["title"];
        var approval = context.StepOutputs["manager-approval"] as ApprovalResult;
        Console.WriteLine($"  [EscalationStep] ⚠️ 升级处理: {title} 被拒绝，原因: {approval?.Comment}");

        context.StepOutputs[StepId] = new { Escalated = true, Reason = approval?.Comment };
        return Complete(context.StepOutputs[StepId]);
    }
}

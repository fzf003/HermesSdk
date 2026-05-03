namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

/// <summary>通知步骤 — 审批通过后发送通知</summary>
public class NotifyAgentStep : CodeStepHandler
{
    public override string StepId => "notify-step";

    public override async Task<StepResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken ct
    )
    {
        var title = context.InitialInput["title"];
        Console.WriteLine($"  [NotifyStep] 📨 采购通知已发送: {title} 已批准");

        context.StepOutputs[StepId] = new { NotificationSent = true, Channel = "email" };
        return Complete(context.StepOutputs[StepId]);
    }
}

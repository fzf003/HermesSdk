using System.Text.Json;

namespace HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

/// <summary>审核 Agent 步骤 — 发送给外部 Agent 审查</summary>
public class ReviewAgentStep : AgentStepHandler
{
    public override string StepId => "review-agent";
    public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
    public override string RouteName => "workflow.review";
    public override string EventType => "workflow.step";

    public override string BuildPrompt(WorkflowContext context)
    {
        var title = context.InitialInput["title"];
        var amount = context.InitialInput["amount"];
        var append =
            "只用Json回复,随机true \r\n{\r\n\"Success\":true, //true \r\n\"Message\":\"\" // 说明原因\r\n}, 回复格式:json";
        return $"请审核以下采购申请:\n标题: {title}\n金额: {amount} 元\r\n{append}";
    }

    public override async Task<StepResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken ct
    )
    {
        if (context.StepOutputs.TryGetValue(StepId, out var output))
        {
            var outputjson = output?.ToString()?.Replace("```json", "").Replace("```", "");
            if (string.IsNullOrWhiteSpace(outputjson))
                return Failed(new Exception("Agent 返回为空"));

            var outmessage = JsonSerializer.Deserialize<OutMessage>(outputjson!);
            if (outmessage?.Success == true)
            {
                context.SetData(StepId, outmessage);
                return Sequential("manager-approval", outmessage);
            }
        }
        return Failed(new Exception("审核未通过"));
    }
}

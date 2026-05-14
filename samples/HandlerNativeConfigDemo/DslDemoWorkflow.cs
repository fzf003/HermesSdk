using HermesAgent.Sdk.WorkflowChain.Dsl;

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// DSL 工作流演示 — 通过 Register&lt;T&gt; 类继承式定义工作流。
/// 运行时导出 YAML 验证代码语义与配置一致性。
/// </summary>
sealed class DslDemoWorkflow : Workflow
{
    private static int _processAttempt;

    public override string Name => "dsl-demo-wf";
    public override string Id => "dsl-demo-wf";
    public override string? Description => "DSL 类继承式工作流定义演示";

    protected override void Build(IStepBuilder builder)
    {
        // Step 1: 入口 — 仅作为起点，输出给下一步
        builder.AddCodeStep("dsl-entry", async (ctx, ct) =>
        {
            await Task.Delay(1, ct);
            return new StepResult { IsSuccess = true, NextStepIds = ["dsl-process"], Output = "entry ok" };
        })
        .WithName("DSL入口");

        // Step 2: 处理步骤 — 演示 timeout + retry 的 Fluent 配置
        builder.AddCodeStep("dsl-process", async (ctx, ct) =>
        {
            _processAttempt++;
            Console.WriteLine($"  🔄 dsl-process: attempt #{_processAttempt} {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");

            var input = ctx.GetOutput<string>("dsl-entry");
            throw new Exception("处理步骤异常");
            await Task.Delay(1, ct);
            return new StepResult { IsSuccess = true, NextStepIds = ["dsl-agent"], Output = $"processed: {input}" };
        })
        .WithName("DSL处理")
        // .WithTimeout("00:00:10")
        .WithRetry(r => r.ExponentialBackoff(initialDelay: "5s", maxDelay: "00:05:00", maxRetries: 3));

        // Step 3: Agent 步骤 — 演示 prompt/system_prompt 的 Fluent 配置
        builder.AddAgentStep("dsl-agent", ctx => new()
        {
            UserPrompt = $"结果为: {ctx.GetOutput<string>("dsl-process")}",
            SystemPrompt = "你是一个 DSL 演示助手",
        })
        .WithSystemPrompt("DSL SystemPrompt（来自 Fluent 配置）")
        .WithName("DSL Agent步骤")
        .WithTimeout("00:00:30");
    }
}

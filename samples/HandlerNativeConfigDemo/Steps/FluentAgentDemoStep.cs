namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// Fluent Agent 配置演示步骤 — 验证 YAML > Fluent > 虚属性。
/// 虚属性：Prompt=null
/// Fluent：Prompt="Fluent API 配置的提示词", Timeout=30s
/// YAML：prompt="YAML 配置的提示词"+timeout=60s（YAML prompt+timeout 胜出）
/// → YAML 全面胜出（验证要求 Agent 步骤必须含 prompt）
/// </summary>
internal sealed class FluentAgentDemoStep : AgentStepHandler
{
    public override string StepId => "fluent-agent";
    public override string RouteName => "demo.route";
    public override string EventType => "demo.event";
    public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;

    /// <summary>虚属性 Prompt=null — Fluent 或 YAML 提供</summary>
    public override string? Prompt => null;

    public override string BuildPrompt(WorkflowContext context) => "回退 BuildPrompt";

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  ✅ FluentAgentDemoStep: Agent 步骤完成");
        Console.WriteLine("     (YAML timeout=60s + prompt > Fluent timeout=30s + prompt → YAML 胜出)");
        return Task.FromResult(Complete());
    }
}

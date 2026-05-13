namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// Agent 步骤的 prompt 配置。由 <c>AddAgentStep(id, cfg)</c> 的 Lambda 返回，
/// 在 <c>BuildPrompt()</c> 时调用生成 prompt。
/// </summary>
public record AgentConfig
{
    /// <summary>系统提示词</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>用户提示词</summary>
    public string? UserPrompt { get; init; }
}

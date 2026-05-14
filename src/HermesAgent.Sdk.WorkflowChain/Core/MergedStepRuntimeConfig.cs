namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤运行时策略合并结果。
/// 仅包含策略与输入模板字段（timeout/retry/error_policy/prompt），
/// 不包含拓扑字段（depends_on/next_step_id/steps/wait_mode）。
/// 拓扑由代码定义，不受 YAML 覆盖。
/// </summary>
public sealed class MergedStepRuntimeConfig
{
    public string? Timeout { get; init; }
    public string? TimeoutAction { get; init; }
    public RetryConfigYaml? Retry { get; init; }
    public string? ErrorPolicy { get; init; }
    public string? Prompt { get; init; }
    public string? SystemPrompt { get; init; }
    public string? RouteName { get; init; }
    public string? EventType { get; init; }
    public ApprovalNotificationConfig? Notification { get; init; }
    public string? HeartbeatExtension { get; init; }
}

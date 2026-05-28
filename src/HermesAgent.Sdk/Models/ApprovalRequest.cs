using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// POST /v1/runs/{run_id}/approval 请求体。
/// </summary>
public record ApprovalRequest
{
    /// <summary>审批决策：once, session, always, deny。</summary>
    [JsonPropertyName("choice")]
    public string Choice { get; set; } = "once";

    /// <summary>是否解析所有挂起的审批请求。默认 false。</summary>
    [JsonPropertyName("all")]
    public bool? All { get; init; } = false;

    public static ApprovalRequest Instance = new ApprovalRequest();

    public ApprovalRequest SetDeny()
    {
        return this with { Choice = "deny", All = false };
    }

    public ApprovalRequest SetAlways()
    {
        return this with { Choice = "always", All = false };
    }

    public ApprovalRequest SetSessions()
    {
        return this with { Choice = "session", All = false };
    }
 }



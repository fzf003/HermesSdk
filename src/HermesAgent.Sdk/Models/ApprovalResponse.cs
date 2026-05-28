using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// POST /v1/runs/{run_id}/approval 的响应。
/// </summary>
public record ApprovalResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "hermes.run.approval_response";

    [JsonPropertyName("run_id")]
    public string RunId { get; init; } = string.Empty;

    [JsonPropertyName("choice")]
    public string Choice { get; init; } = string.Empty;

    /// <summary>成功解析的审批请求数量。</summary>
    [JsonPropertyName("resolved")]
    public int Resolved { get; init; }
}

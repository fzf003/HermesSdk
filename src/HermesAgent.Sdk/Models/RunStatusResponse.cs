using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// GET /v1/runs/{run_id} 返回的运行状态。
/// </summary>
public record RunStatusResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "hermes.run";

    [JsonPropertyName("run_id")]
    public string RunId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>创建时间（Unix 时间戳）。</summary>
    [JsonPropertyName("created_at")]
    public double? CreatedAt { get; init; }

    /// <summary>更新时间（Unix 时间戳）。</summary>
    [JsonPropertyName("updated_at")]
    public double? UpdatedAt { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("last_event")]
    public string? LastEvent { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

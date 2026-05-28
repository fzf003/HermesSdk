using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// POST /v1/runs/{run_id}/stop 的响应。
/// </summary>
public record StopRunResponse
{
    [JsonPropertyName("run_id")]
    public string RunId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

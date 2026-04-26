using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record RunStartResponse
{
    [JsonPropertyName("run_id")]
    public string RunId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

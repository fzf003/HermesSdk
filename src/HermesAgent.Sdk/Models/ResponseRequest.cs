using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record ResponseRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "default";

    [JsonPropertyName("input")]
    public required string Input { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("previous_response_id")]
    public string? PreviousResponseId { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

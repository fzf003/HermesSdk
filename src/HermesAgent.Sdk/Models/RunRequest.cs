using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record RunRequest
{
    [JsonPropertyName("input")]
    public required string Prompt { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = "default";

    [JsonPropertyName("skills")]
    public List<string>? Skills { get; init; }

    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

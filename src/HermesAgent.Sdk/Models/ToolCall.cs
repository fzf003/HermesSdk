using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record ToolCall
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?>? Arguments { get; init; }
}

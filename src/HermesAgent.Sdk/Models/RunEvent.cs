using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record RunEvent
{
    [JsonPropertyName("event")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; init; }

    [JsonPropertyName("output")]
    public string OutPut { get; init; } = string.Empty;
    /// <summary>
    /// Ë¼¿¼Á´
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

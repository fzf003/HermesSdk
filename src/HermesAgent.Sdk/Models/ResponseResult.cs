using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record ResponseResult
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; init; } = "response";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("output")]
    public List<OutputItem> Output { get; init; } = new();

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }
}

public record OutputItem
{

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public List<OutPutContent> Contents { get; init; } = new();

}


public record OutPutContent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
    
}
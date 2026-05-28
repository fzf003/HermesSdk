namespace HermesAgent.Sdk;

public record ResponseOptions
{
    public string? Model { get; init; } = "default";
    public string? Instructions { get; init; }
    public string? Conversation { get; init; }
    public int? MaxOutputTokens { get; init; } = 1024;
    public float? Temperature { get; init; } = 0.7f;
    public Dictionary<string, string>? Metadata { get; init; } = new();
}

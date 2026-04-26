namespace HermesAgent.Sdk;

public record ResponseOptions
{
    public string? Model { get; init; }
    public string? Instructions { get; init; }
    public int? MaxOutputTokens { get; init; }
    public float? Temperature { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

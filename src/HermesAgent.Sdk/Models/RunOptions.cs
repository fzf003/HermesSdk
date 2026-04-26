namespace HermesAgent.Sdk;

public record RunOptions
{
    public string? Model { get; init; }
    public List<string>? Skills { get; init; }
    public int? MaxIterations { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

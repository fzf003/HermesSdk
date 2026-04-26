namespace HermesAgent.Sdk;

public record RunResult
{
    public string RunId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Output { get; init; }
    public int? DurationMs { get; init; }
    public int? ToolCallCount { get; init; }
    public string? ErrorMessage { get; init; }
}

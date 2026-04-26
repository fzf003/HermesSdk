namespace HermesAgent.Sdk;

public record WebhookOptions
{
    public Dictionary<string, string>? Headers { get; init; }
    public string? IdempotencyKey { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? SignatureSecret { get; init; }
}

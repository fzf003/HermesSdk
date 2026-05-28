namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// Configuration options for the Microsoft Agent Framework (MAF) integration.
/// Bound from the <c>"HermesAgent:Maf"</c> configuration section.
/// </summary>
public class HermesAgentMafOptions
{
    /// <summary>
    /// Whether to enable <see cref="AutoSessionMiddleware"/> in the <c>IChatClient</c> pipeline.
    /// When enabled, requests without a <c>hermes-conversation-id</c> automatically receive
    /// a generated Topic ID (<c>"topic-{Guid}"</c>).
    /// </summary>
    public bool EnableAutoSession { get; set; } = false;

    /// <summary>
    /// Whether to enable <see cref="HermesRunMiddleware"/> in the <c>IChatClient</c> pipeline.
    /// When enabled, long-running tasks are routed through Hermes Run + SSE mode
    /// if the <c>ChatOptions</c> carries the <c>hermes-use-run</c> flag.
    /// </summary>
    public bool EnableRunMiddleware { get; set; } = false;

    /// <summary>
    /// Whether to enable OpenTelemetry instrumentation in the <c>IChatClient</c> pipeline.
    /// </summary>
    public bool EnableOpenTelemetry { get; set; } = false;
}

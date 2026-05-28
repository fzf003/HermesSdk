using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record RunRequest
{
    [JsonPropertyName("input")]
    public required string Prompt { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = "default";

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("previous_response_id")]
    public string? PreviousResponseId { get; init; }

    [JsonPropertyName("conversation_history")]
    public List<ChatMessage>? ConversationHistory { get; init; }

    [JsonPropertyName("skills")]
    public List<string>? Skills { get; init; }

    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

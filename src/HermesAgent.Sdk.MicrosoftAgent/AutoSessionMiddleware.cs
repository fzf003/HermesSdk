using Microsoft.Extensions.AI;

namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// A <see cref="DelegatingChatClient"/> middleware that automatically injects a
/// <c>hermes-conversation-id</c> (Topic ID) into <see cref="Microsoft.Extensions.AI.ChatOptions.AdditionalProperties"/>
/// when one is not already present.
///
/// <para>
/// The generated ID follows the format <c>"topic-{Guid:N}"</c> (e.g.
/// <c>"topic-550e8400e29b41d4a716446655440000"</c>).
/// When an ID is already present, the middleware passes it through unchanged.
/// </para>
///
/// <para>
/// Place this middleware early in the <c>ChatClientBuilder</c> pipeline so that
/// downstream components always have a consistent conversation identifier.
/// </para>
/// </summary>
[Obsolete("No longer needed. HermesAgent from HermesAgent.Sdk.AgentAdapter manages " +
          "conversation IDs natively via AgentSession.ConversationId.")]
public sealed class AutoSessionMiddleware : DelegatingChatClient
{
    private const string ConversationIdKey = "hermes-conversation-id";

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    /// <param name="innerClient">The inner <see cref="IChatClient"/> to delegate to.</param>
    public AutoSessionMiddleware(IChatClient innerClient)
        : base(innerClient)
    {
    }

    /// <inheritdoc />
    public override Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConversationId(options);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConversationId(options);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private static void EnsureConversationId(Microsoft.Extensions.AI.ChatOptions? options)
    {
        if (options is null) return;

        options.AdditionalProperties ??= [];

        if (options.AdditionalProperties.ContainsKey(ConversationIdKey))
            return;

        options.AdditionalProperties[ConversationIdKey] = $"topic-{Guid.NewGuid():N}";
    }
}

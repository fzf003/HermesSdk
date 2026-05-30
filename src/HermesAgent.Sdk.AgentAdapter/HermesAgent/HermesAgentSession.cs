using Microsoft.Agents.AI;

namespace HermesAgent.Sdk.AgentAdapter;

/// <summary>
/// Session implementation for <see cref="HermesAgent"/>.
/// Carries the Hermes <c>conversation</c> value as <see cref="ConversationId"/>,
/// stored in the <see cref="AgentSession.StateBag"/>.
/// </summary>
/*
public class HermesAgentSession : AgentSession
{
    private const string ConversationIdKey = "hermes-conversation-id";

    /// <summary>
    /// Initializes a new instance of <see cref="HermesAgentSession"/>.
    /// </summary>
    public HermesAgentSession() : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HermesAgentSession"/> with a specific conversation ID.
    /// Use this to resume an existing conversation without going through CreateSessionAsync.
    /// </summary>
    /// <param name="conversationId">The Hermes conversation ID (topic ID) to resume.</param>
    public HermesAgentSession(string conversationId) : base()
    {
        ConversationId = conversationId;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HermesAgentSession"/> with an existing state bag.
    /// </summary>
    public HermesAgentSession(AgentSessionStateBag stateBag) : base(stateBag)
    {
    }

    /// <summary>
    /// Gets or sets the Hermes conversation ID (topic ID).
    /// Persisted in <see cref="AgentSession.StateBag"/>.
    /// </summary>
    public string? ConversationId
    {
        get
        {
            if (StateBag is null) return null;
            if (StateBag.TryGetValue<string>(ConversationIdKey, out var id, null))
                return id;
            return null;
        }
        set
        {
            if (value is not null)
                StateBag.SetValue(ConversationIdKey, value, null);
            else
                StateBag.TryRemoveValue(ConversationIdKey);
        }
    }
}
*/
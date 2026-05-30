using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.AgentAdapter;

/// <summary>
/// A custom <see cref="AIAgent"/> that communicates directly with the Hermes
/// Responses API (<c>POST /v1/responses</c>), avoiding the <c>IChatClient</c>
/// abstraction mismatch that plagues the adapter-based approach.
///
/// <para>
/// Session management maps cleanly: <see cref="HermesAgentSession.ConversationId"/>
/// IS the Hermes <c>conversation</c> value (topic ID). No more
/// <c>previous_response_id</c> tracking, no more <c>AutoSessionMiddleware</c>
/// hacks.
/// </para>
///
/// <para>
/// Function calling (<c>function_call</c> / <c>function_call_output</c>) is
/// handled at the output mapping layer — the server executes tools and returns
/// results; this agent does not implement the client-side tool execution loop.
/// </para>
/// </summary>
/*
public class HermesAgent : AIAgent
{
    private readonly string _agentName;
    private readonly string? _agentDescription;
    private readonly IHermesResponseClient _responseClient;
    private readonly ILogger<HermesAgent> _logger;
    private readonly string? _model;
    private readonly string? _instructions;
    private readonly int? _maxOutputTokens;
    private readonly float? _temperature;

    /// <summary>
    /// Initializes a new instance of <see cref="HermesAgent"/>.
    /// </summary>
    /// <param name="name">Agent display name.</param>
    /// <param name="description">Optional agent description.</param>
    /// <param name="responseClient">Hermes Responses API client.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="model">Model identifier (e.g. "default").</param>
    /// <param name="instructions">Optional system instructions.</param>
    /// <param name="maxOutputTokens">Maximum output token count.</param>
    /// <param name="temperature">Sampling temperature.</param>
    public HermesAgent(
        string name,
        string? description,
        IHermesResponseClient responseClient,
        ILogger<HermesAgent> logger,
        string? model = "default",
        string? instructions = null,
        int? maxOutputTokens = 1024,
        float? temperature = 0.7f)
        : base()
    {
        _agentName = name;
        _agentDescription = description;
        _responseClient = responseClient;
        _logger = logger;
        _model = model;
        _instructions = instructions;
        _maxOutputTokens = maxOutputTokens;
        _temperature = temperature;
    }

    /// <inheritdoc />
    public override string Name => _agentName;

    /// <inheritdoc />
    public override string? Description => _agentDescription;

    // ──────────────────────────────────────────────
    //  Session management
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new session or resumes an existing one.
    /// </summary>
    /// <param name="conversationId">
    /// Optional conversation ID to resume. If null or empty, a new session with
    /// a fresh topic ID is generated. In web scenarios, pass the client-supplied
    /// session ID here to continue an existing conversation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="AgentSession"/> ready for passing to RunAsync.</returns>
    public async Task<AgentSession> CreateSessionAsync(string? conversationId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(conversationId))
            return new HermesAgentSession(conversationId);

        return await CreateSessionAsync(cancellationToken);
    }

    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        return new HermesAgentSession();
    }
 
   

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        // Deserialize into a state bag and wrap in HermesAgentSession
        var stateBag = serializedState.Deserialize<AgentSessionStateBag>(jsonSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize AgentSessionStateBag");
        var session = new HermesAgentSession(stateBag);
        return ValueTask.FromResult<AgentSession>(session);
    }

    /// <inheritdoc />
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToElement(session.StateBag, jsonSerializerOptions);
        return ValueTask.FromResult(json);
    }

    // ──────────────────────────────────────────────
    //  Non-streaming run
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        var input = ExtractInput(messages);
        var responseOptions = ToResponseOptions(session, options);

        ResponseResult result;
        try
        {
            result = await _responseClient.CreateAsync(input, responseOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Hermes Responses API response parsing failed", ex);
        }

        // 如果服务返回了 conversation（topic id），并且调用方传入了 AgentSession，
        // 将 conversation 写回到 session，以便下一轮使用同一话题继续会话。
        if (session is HermesAgentSession hermesSession && !string.IsNullOrEmpty(result.Conversation))
        {
            hermesSession.ConversationId = result.Conversation;
        }

        var contents = MapOutputToContents(result.Output);

        return new AgentResponse
        {
            AgentId = Id,
            ResponseId = result.Id,
            Messages =
            [
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, contents),
            ],
        };
    }

    // ──────────────────────────────────────────────
    //  Streaming run (SSE)
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var input = ExtractInput(messages);
        var responseOptions = ToResponseOptions(session, options);

        await foreach (var data in _responseClient.CreateStreamingAsync(input, responseOptions, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // 解析是否包含 response.conversation 字段（例如 response.completed 事件中可能包含）。
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("response", out var respElem) && respElem.ValueKind == JsonValueKind.Object)
                {
                    if (respElem.TryGetProperty("conversation", out var convProp))
                    {
                        var conv = convProp.GetString();
                        if (!string.IsNullOrEmpty(conv) && session is HermesAgentSession hs)
                        {
                            hs.ConversationId = conv;
                        }
                    }
                }
            }
            catch (JsonException) { }

            var processingResult = ProcessSseEvent(data);

            if (processingResult.ThrowError is not null)
                throw new InvalidOperationException($"Hermes Responses API error: {processingResult.ThrowError}");

            if (processingResult.ShouldBreak)
                yield break;

            if (processingResult.Update is not null)
                yield return processingResult.Update;
        }
    }

    private const string ConversationIdKey = "hermes-conversation-id";

    // ──────────────────────────────────────────────
    //  Session / Options helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Extracts the input text for the Hermes API from the message list.
    /// Only the last user message is used; Hermes server manages history
    /// via the <c>conversation</c> field.
    /// </summary>
    private static string ExtractInput(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        return messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
    }

    /// <summary>
    /// Converts the session state and run options into a <see cref="ResponseOptions"/>
    /// suitable for the Hermes Responses API.
    ///
    /// Conversation ID resolution order:
    ///   1. <see cref="HermesAgentSession.ConversationId"/> (from session)
    ///   2. <c>options.AdditionalProperties["hermes-conversation-id"]</c> (per-request convenience)
    /// </summary>
    private ResponseOptions ToResponseOptions(AgentSession? session, AgentRunOptions? options)
    {
        // Resolve conversation ID: session takes priority, fall back to AdditionalProperties
        var conversationId = (session as HermesAgentSession)?.ConversationId;
        if (string.IsNullOrEmpty(conversationId) && options?.AdditionalProperties is not null)
        {
            if (options.AdditionalProperties.TryGetValue(ConversationIdKey, out var raw) && raw is string s)
                conversationId = s;
        }

        // Build metadata from AdditionalProperties (exclude the conversation ID key)
        Dictionary<string, string>? metadata = null;
        if (options?.AdditionalProperties is { } props && props.Count > 0)
        {
            metadata = props
                .Where(kv => kv.Value is not null && kv.Key != ConversationIdKey)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString()!);
        }

        return new ResponseOptions
        {
            Model = _model,
            Instructions = _instructions,
            Conversation = conversationId,
            MaxOutputTokens = _maxOutputTokens,
            Temperature = _temperature,
            Metadata = metadata,
        };
    }

    // ──────────────────────────────────────────────
    //  Output mapping: OutputItem → AIContent
    // ──────────────────────────────────────────────

    /// <summary>
    /// Maps Hermes Responses API <see cref="OutputItem"/> list to MAF <see cref="AIContent"/> list.
    ///
    /// Mapping rules:
    /// <list type="bullet">
    ///   <item><c>"function_call"</c> → <see cref="FunctionCallContent"/></item>
    ///   <item><c>"function_call_output"</c> → <see cref="FunctionResultContent"/></item>
    ///   <item><c>"message"</c> / <c>"text"</c> / <c>"output_text"</c> → <see cref="TextContent"/></item>
    /// </list>
    /// </summary>
    private static List<AIContent> MapOutputToContents(List<OutputItem> output)
    {
        var contents = new List<AIContent>();

        foreach (var item in output)
        {
            switch (item.Type)
            {
                case "function_call":
                {
                    var props = item.AdditionalProperties;
                    var callId = props?.TryGetValue("call_id", out var cid) == true ? cid.GetString() : null;
                    var name = props?.TryGetValue("name", out var n) == true ? n.GetString() : null;
                    Dictionary<string, object?>? argsDict = null;
                    if (props?.TryGetValue("arguments", out var a) == true && a.ValueKind == JsonValueKind.Object)
                    {
                        argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(a.GetRawText());
                    }
                    contents.Add(new FunctionCallContent(callId!, name!, argsDict));
                    break;
                }

                case "function_call_output":
                {
                    var props = item.AdditionalProperties;
                    var callId = props?.TryGetValue("call_id", out var cid) == true ? cid.GetString() : null;
                    var outputText = props?.TryGetValue("output", out var o) == true ? o.GetString() : null;
                    contents.Add(new FunctionResultContent(callId!, outputText));
                    break;
                }

                default:
                {
                    foreach (var content in item.Contents)
                    {
                        if (content.Type is "text" or "output_text")
                        {
                            contents.Add(new TextContent(content.Text));
                        }
                    }
                    break;
                }
            }
        }

        return contents;
    }

    // ──────────────────────────────────────────────
    //  SSE event processing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Processes a single SSE data line and returns the appropriate event result.
    /// </summary>
    private SseEventResult ProcessSseEvent(string data)
    {
        try
        {
            using var eventDoc = JsonDocument.Parse(data);
            var eventRoot = eventDoc.RootElement;

            var eventType = eventRoot.TryGetProperty("type", out var t) ? t.GetString() : null;
            eventType ??= eventRoot.TryGetProperty("event", out var e) ? e.GetString() : null;

            switch (eventType)
            {
                case "response.output_text.delta":
                {
                    var deltaText = eventRoot.GetProperty("delta").GetString();
                    if (!string.IsNullOrEmpty(deltaText))
                    {
                        return SseEventResult.Yield(
                            new AgentResponseUpdate
                            {
                                Role = ChatRole.Assistant,
                                Contents = [new TextContent(deltaText)],
                            });
                    }
                    return SseEventResult.None();
                }

                case "response.output_item.added":
                    return SseEventResult.None();

                case "response.completed":
                    return SseEventResult.Complete();

                case "error":
                {
                    var errorMsg = eventRoot.TryGetProperty("error", out var err)
                        ? err.GetString()
                        : "Unknown Responses API error";
                    return SseEventResult.Error(errorMsg!);
                }

                default:
                    _logger.LogDebug("Skipping unknown Responses API SSE event: {EventType}", eventType);
                    return SseEventResult.None();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Responses API SSE data: {Data}", data);
            return SseEventResult.None();
        }
    }

    /// <summary>
    /// Represents the result of processing a single SSE event.
    /// </summary>
    private readonly struct SseEventResult
    {
        public AgentResponseUpdate? Update { get; }
        public bool ShouldBreak { get; }
        public string? ThrowError { get; }

        private SseEventResult(AgentResponseUpdate? update, bool shouldBreak, string? throwError)
        {
            Update = update;
            ShouldBreak = shouldBreak;
            ThrowError = throwError;
        }

        public static SseEventResult Yield(AgentResponseUpdate update) => new(update, false, null);
        public static SseEventResult Complete() => new(null, true, null);
        public static SseEventResult Error(string message) => new(null, false, message);
        public static SseEventResult None() => default;
    }
}
*/
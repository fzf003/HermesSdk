using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HermesChatMessage = HermesAgent.Sdk.ChatMessage;
// HermesAgent.Sdk.MicrosoftAgent is a child namespace of HermesAgent.Sdk.
// Therefore unqualified ChatMessage/ChatOptions/ChatResponse resolve to the
// HermesAgent.Sdk types.  We alias the MAF names so the *body* of each
// method is readable; the method *signatures* (overrides / interface impl)
// MUST spell the MAF types fully to match the base definition correctly.
using MafChatMessage = Microsoft.Extensions.AI.ChatMessage;
using MafChatOptions = Microsoft.Extensions.AI.ChatOptions;
using MafChatResponse = Microsoft.Extensions.AI.ChatResponse;
using MafChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;

namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// An <see cref="IChatClient"/> adapter that translates between the Microsoft Agent Framework (MAF)
/// abstraction and the Hermes Agent SDK.
///
/// <para>Single-path routing (Responses API):</para>
/// <list type="bullet">
///   <item><description>所有请求统一通过 <see cref="IHermesResponseClient"/> 调用 <c>/v1/responses</c>。</description></item>
///   <item><description>会话通过 <c>conversation</c> key 管理，Server 自动关联上下文历史。</description></item>
///   <item><description><see cref="IHermesChatClient"/> 保留注入但不再用于主要路由（作为备援）。</description></item>
/// </list>
/// <para>Session state is managed entirely by the Hermes Server via the <c>conversation</c> key —
/// the adapter only passes it through and does not track any session state on the client.</para>
/// </summary>
public class HermesChatClientAdapter : IChatClient
{
    private readonly IHermesChatClient _chatClient;
    private readonly ILogger<HermesChatClientAdapter> _logger;
    private readonly IHermesResponseClient _responseClient;

    // 会话状态由 Hermes Server 维护，客户端不跟踪任何会话数据

    /// <summary>
    /// Initializes the adapter.
    /// </summary>
    public HermesChatClientAdapter(
        IHermesChatClient chatClient,
        ILogger<HermesChatClientAdapter> logger,
        IHermesResponseClient responseClient)
    {
        _chatClient = chatClient;
        _logger = logger;
        _responseClient = responseClient;
    }

    // ──────────────────────────────────────────────
    //  IChatClient — Non-streaming
    //  NOTE: method signatures use fully-qualified
    //  MAF types to match the interface correctly.
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messagesList = messages.ToList();
        var conversationId = GetConversationId(options);

        // 统一通过 Responses API 处理所有请求（含无 tools 场景）
        return await GetResponseViaResponsesApiAsync(conversationId, messagesList, options, cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  IChatClient — Streaming
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messagesList = messages.ToList();
        var conversationId = GetConversationId(options);

        // 统一通过 Responses API 处理所有流式请求
        await foreach (var update in GetStreamingResponseViaResponsesApiAsync(conversationId, messagesList, options, cancellationToken))
        {
            yield return update;
        }
    }

    // ──────────────────────────────────────────────
    //  IChatClient — GetService / Dispose
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
        // No managed resources to release.
    }

    // ──────────────────────────────────────────────
    //  Chat Completions path
    // ──────────────────────────────────────────────

    private async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseViaChatCompletionsAsync(
        string? conversationId,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken ct)
    {
        if (conversationId is not null)
            _chatClient.SetSession(conversationId);

        var request = BuildChatRequest(messages, options, stream: false);
        var response = await _chatClient.ChatAsync(request, ct);

        var mafMessages = new List<Microsoft.Extensions.AI.ChatMessage>();
        foreach (var choice in response.Choices)
        {
            mafMessages.Add(MapToMafMessage(choice.Message));
        }

        var mafResponse = new Microsoft.Extensions.AI.ChatResponse(mafMessages)
        {
            Usage = MapToMafUsage(response.Usage),
        };
        return mafResponse;
    }

    private async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseViaChatCompletionsAsync(
        string? conversationId,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversationId is not null)
            _chatClient.SetSession(conversationId);

        var request = BuildChatRequest(messages, options, stream: true);

        await foreach (var chunk in _chatClient.ChatStreamAsync(request, ct))
        {
            var delta = chunk.Choices?.FirstOrDefault()?.Delta;
            if (delta is null)
                continue;

            if (!string.IsNullOrEmpty(delta.Content))
            {
                yield return new Microsoft.Extensions.AI.ChatResponseUpdate(null, delta.Content);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Responses API path — non-streaming
    // ──────────────────────────────────────────────

    private async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseViaResponsesApiAsync(
        string? conversationId,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken ct)
    {
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var input = lastUserMsg?.Text ?? string.Empty;
        var responseOptions = ToResponseOptions(options, conversationId);

        ResponseResult result;
        try
        {
            result = await _responseClient.CreateAsync(input, responseOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse Hermes Responses API response", ex);
        }

        var mafMessage = new Microsoft.Extensions.AI.ChatMessage { Role = ChatRole.Assistant };
        var contents = new List<AIContent>();

        foreach (var item in result.Output)
        {
            foreach (var content in item.Contents)
            {
                if (content.Type is "text" or "output_text")
                {
                    contents.Add(new TextContent(content.Text));
                }
            }
        }

        if (contents.Count > 0)
        {
            mafMessage.Contents = contents;
        }

        return new Microsoft.Extensions.AI.ChatResponse(new[] { mafMessage })
        {
            Usage = MapToMafUsage(result.Usage),
        };
    }

    // ──────────────────────────────────────────────
    //  Responses API path — streaming
    // ──────────────────────────────────────────────

    private async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseViaResponsesApiAsync(
        string? conversationId,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var input = lastUserMsg?.Text ?? string.Empty;
        var responseOptions = ToResponseOptions(options, conversationId);

        await foreach (var data in _responseClient.CreateStreamingAsync(input, responseOptions, ct))
        {
            var result = ProcessSseEvent(data, options);

            if (result.ThrowError is not null)
                throw new InvalidOperationException($"Hermes Responses API error: {result.ThrowError}");

            if (result.FinalText is not null)
            {
                yield return new Microsoft.Extensions.AI.ChatResponseUpdate(null, new List<Microsoft.Extensions.AI.AIContent> { new Microsoft.Extensions.AI.TextContent(result.FinalText) });
            }
            
            if (result.ShouldBreak)
                yield break;

            if (result.Update is not null)
                yield return result.Update;
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// 获取 conversation_id，优先级：
    /// 1. ChatOptions.AdditionalProperties["hermes-conversation-id"]（显式传入）
    /// 2. HermesContext.GetConversationId()（隐式上下文回退）
    /// 3. 均无返回 null（Server 创建新会话）
    /// </summary>
    private static string? GetConversationId(Microsoft.Extensions.AI.ChatOptions? options)
    {
        if (options?.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var val) == true
            && val is string s && !string.IsNullOrEmpty(s))
            return s;

        return HermesContext.GetConversationId();
    }

    private static HermesAgent.Sdk.ChatRequest BuildChatRequest(
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        bool stream)
    {
        var hermesMessages = new List<HermesChatMessage>(messages.Count);
        foreach (var msg in messages)
        {
            hermesMessages.Add(MapToHermesMessage(msg));
        }

        return new HermesAgent.Sdk.ChatRequest
        {
            Model = options?.ModelId ?? "default",
            Messages = hermesMessages,
            Stream = stream,
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            MaxTokens = options?.MaxOutputTokens,
            Stop = options?.StopSequences?.ToList(),
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
        };
    }

    private static ResponseOptions ToResponseOptions(Microsoft.Extensions.AI.ChatOptions? options, string? conversationId)
    {
        return new ResponseOptions
        {
            Model = options?.ModelId,
            Instructions = options?.Instructions,
            Conversation = conversationId,
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            Metadata = options?.AdditionalProperties is { } props
                ? props.Where(kv => kv.Value is not null)
                       .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString()!)
                : null,
        };
    }

    private static HermesChatMessage MapToHermesMessage(Microsoft.Extensions.AI.ChatMessage msg)
    {
        var role = msg.Role.Value switch
        {
            "assistant" => "assistant",
            "user" => "user",
            "system" => "system",
            "tool" => "tool",
            _ => "user",
        };

        var content = msg.Contents is { Count: > 0 }
            ? string.Join("", msg.Contents.OfType<TextContent>().Select(c => c.Text))
            : msg.Text ?? string.Empty;

        return new HermesChatMessage(role, content);
    }

    private static Microsoft.Extensions.AI.ChatMessage MapToMafMessage(HermesChatMessage msg)
    {
        var role = msg.Role switch
        {
            "assistant" => ChatRole.Assistant,
            "user" => ChatRole.User,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => ChatRole.Assistant,
        };

        return new Microsoft.Extensions.AI.ChatMessage(role, msg.Content ?? string.Empty);
    }

    private static Microsoft.Extensions.AI.UsageDetails? MapToMafUsage(HermesAgent.Sdk.UsageInfo? usage)
    {
        if (usage is null) return null;

        return new Microsoft.Extensions.AI.UsageDetails
        {
            InputTokenCount = usage.PromptTokens,
            OutputTokenCount = usage.CompletionTokens,
            TotalTokenCount = usage.TotalTokens,
        };
    }

    private readonly struct SseEventResult
    {
        public Microsoft.Extensions.AI.ChatResponseUpdate? Update { get; }
        public string? FinalText { get; }
        public bool ShouldBreak { get; }
        public string? ThrowError { get; }

        private SseEventResult(Microsoft.Extensions.AI.ChatResponseUpdate? update, string? finalText, bool shouldBreak, string? throwError)
        {
            Update = update;
            FinalText = finalText;
            ShouldBreak = shouldBreak;
            ThrowError = throwError;
        }

        public static SseEventResult Yield(Microsoft.Extensions.AI.ChatResponseUpdate update) => new(update, null, false, null);
        public static SseEventResult Complete(string? finalText = null) => new(null, finalText, true, null);
        public static SseEventResult Error(string message) => new(null, null, false, message);
        public static SseEventResult None() => default;
    }

    private SseEventResult ProcessSseEvent(string data, Microsoft.Extensions.AI.ChatOptions? options)
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
                            var update =new Microsoft.Extensions.AI.ChatResponseUpdate(null, new List<Microsoft.Extensions.AI.AIContent> { new Microsoft.Extensions.AI.TextContent(deltaText) });
                            update.ConversationId= HermesContext.GetConversationId();
                            return SseEventResult.Yield(update);
                        }
                            
                        return SseEventResult.None();
                    }

                case "response.output_item.added":
                    {
                        if (eventRoot.TryGetProperty("item", out var item))
                        {
                            var itemType = item.GetProperty("type").GetString();
                            // 已由 Hermes 服务端执行，跳过 function_call，避免 ChatClientAgent 重复调用
                            if (itemType == "function_call")
                                return SseEventResult.None();
                        }
                        return SseEventResult.None();
                    }

                case "response.completed":
                {
                    string? finalText = null;
                    if (eventRoot.TryGetProperty("response", out var completedResp))
                    {
                        if (completedResp.TryGetProperty("output", out var output))
                        {
                            foreach (var item in output.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var itemType)
                                    && itemType.GetString() == "message"
                                    && item.TryGetProperty("content", out var content))
                                {
                                    foreach (var contentItem in content.EnumerateArray())
                                    {
                                        if (contentItem.TryGetProperty("type", out var ct)
                                            && (ct.GetString() == "text" || ct.GetString() == "output_text")
                                            && contentItem.TryGetProperty("text", out var textProp))
                                        {
                                            var text = textProp.GetString();
                                            if (!string.IsNullOrEmpty(text))
                                                finalText = text;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return SseEventResult.Complete(finalText);
                }

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
}

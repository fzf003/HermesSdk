using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;

/// <summary>
/// Hermes SDK 与 Microsoft Agent Framework (MAF) 之间的 IChatClient 适配器。
///
/// <para>职责：将 MAF 标准的 IChatClient 调用转换为 Hermes Responses API (/v1/responses) 请求。</para>
///
/// <para>架构说明：</para>
/// <list>
///   <item><description>所有请求统一走 Responses API 路径（无聊天补全路径），由 IHermesResponseClient 处理。</description></item>
///   <item><description>会话状态通过 hermes-conversation-id（Topic ID）管理，服务端自动关联 store。</description></item>
///   <item><description>function_call 由服务端执行，客户端仅做结果映射（FunctionCallContent / FunctionResultContent）。</description></item>
///   <item><description>SSE 流式响应解析为 ChatResponseUpdate 序列。</description></item>
/// </list>
/// </summary>
 
public class HermesClientAdapter : IChatClient
{
    private readonly ILogger<HermesClientAdapter> _logger;
    private readonly IHermesResponseClient _responseClient;

    /// <summary>
    /// AdditionalProperties 中存储会话标识的键名。
    /// 该值是一个 Topic ID（如 "topic-550e8400e29b41d4a716446655440000"），
    /// 由 AutoSessionMiddleware 自动生成，或由调用方显式传入。
    /// </summary>
    const string ConversationIdKey = "hermes-conversation-id";

    /// <summary>
    /// 内部持久化 conversation ID，key 为固定的 default。
    /// 用于跨请求保持会话关联（ChatOptions.AdditionalProperties 在请求间会丢失）。
    /// </summary>
    private string? _persistedConversationId;

    /// <summary>
    /// 初始化适配器。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="responseClient">Hermes Responses API 客户端。</param>
    public HermesClientAdapter(
        ILogger<HermesClientAdapter> logger,
        IHermesResponseClient responseClient)
    {
        _logger = logger;
        _responseClient = responseClient;
    }

    // ──────────────────────────────────────────────
    //  IChatClient — 非流式响应
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messagesList = messages.ToList();

        return await GetResponseViaResponsesApiAsync(messagesList, options, cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  IChatClient — 流式响应（SSE）
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messagesList = messages.ToList();

        await foreach (var update in GetStreamingResponseViaResponsesApiAsync(messagesList, options, cancellationToken))
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
    }

    // ──────────────────────────────────────────────
    //  Responses API 路径 — 非流式
    // ──────────────────────────────────────────────

    /// <summary>
    /// 通过 Hermes Responses API (POST /v1/responses) 发送请求并返回结构化响应。
    ///
    /// 处理流程：
    ///   1. 提取最后一条用户消息作为 input
    ///   2. 将 MAF ChatOptions 转换为 Hermes ResponseOptions（含会话 ID 映射）
    ///   3. 调用 ResponseClient.CreateAsync 发送请求
    ///   4. 将 Hermes 返回的 OutputItem 列表映射为 MAF AIContent（文本 / 函数调用）
    ///   5. 将响应 ID 写回 AdditionalProperties 供上游中间件观察
    /// </summary>
    private async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseViaResponsesApiAsync(
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken ct)
    {
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        dynamic input = lastUserMsg?.Text ?? string.Empty;

        #region 媒体信息扩展
        if (messages.HasMediaContent())
        {

            var item = new RequestItem("user");
            var summary = messages.GetMediaSummary();
            foreach (var uri in summary.UriContents)
            {
                 //Console.WriteLine($"  图片URI: {uri.Uri}  类型: {uri.MediaType}");
            }
        }
        #endregion

        var responseOptions = ToResponseOptions(options);

        ResponseResult result;
        try
        {
            result = await _responseClient.CreateAsync(input, responseOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Hermes Responses API 响应解析失败", ex);
        }

        // 将服务端返回的 Conversation ID 写回 ChatOptions.AdditionalProperties 并持久化
        // 确保多轮对话时关联到同一话题
        if (!string.IsNullOrEmpty(result.Conversation))
        {
            options ??= new();
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[ConversationIdKey] = result.Conversation;
            _persistedConversationId = result.Conversation;
        }

        var mafMessage = new Microsoft.Extensions.AI.ChatMessage { Role = ChatRole.Assistant };
        var contents = MapOutputToContents(result.Output);

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
    //  Responses API 路径 — 流式（SSE）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 通过 Hermes Responses API 流式接口获取 SSE 事件流，逐事件映射为 ChatResponseUpdate。
    ///
    /// SSE 事件类型处理：
    ///   - response.output_text.delta → 文本增量（实时输出），逐段 yield
    ///   - response.output_item.added → 跳过（function_call 已在服务端执行）
    ///   - response.completed → 终止流（文本已在 delta 中输出，不重复 yield）
    ///   - error → 抛出异常
    /// </summary>
    private async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseViaResponsesApiAsync(
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var input = lastUserMsg?.Text ?? string.Empty;
        var responseOptions = ToResponseOptions(options);

        await foreach (var data in _responseClient.CreateStreamingAsync(input, responseOptions, ct))
        {
            var result = ProcessSseEvent(data, options);

            if (result.ThrowError is not null)
                throw new InvalidOperationException($"Hermes Responses API 错误: {result.ThrowError}");

            if (result.ShouldBreak)
                yield break;

            if (result.Update is not null)
                yield return result.Update;
        }
    }

    // ──────────────────────────────────────────────
    //  Hermes OutputItem → MAF AIContent 映射
    // ──────────────────────────────────────────────

    /// <summary>
    /// 将 Hermes Responses API 返回的 OutputItem 列表映射为 MAF 的 AIContent 列表。
    ///
    /// 映射规则：
    /// <list type="bullet">
    ///   <item><description>"function_call" → FunctionCallContent（callId, name, arguments 从 AdditionalProperties 中读取）</description></item>
    ///   <item><description>"function_call_output" → FunctionResultContent（callId 和 output 从 AdditionalProperties 中读取）</description></item>
    ///   <item><description>"message"/"text"/"output_text" → TextContent</description></item>
    /// </list>
    ///
    /// 注意：function_call 的额外字段（call_id, name, arguments）通过 OutputItem 的
    /// [JsonExtensionData] AdditionalProperties 传入，而非 Contents 列表。
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
                    // 从 JsonExtensionData 中提取函数调用元数据
                    var props = item.AdditionalProperties;
                    var callId = props?.TryGetValue("call_id", out var cid) == true ? cid.GetString() : null;
                    var name = props?.TryGetValue("name", out var n) == true ? n.GetString() : null;
                    Dictionary<string, object?>? argsDict = null;
                    if (props?.TryGetValue("arguments", out var a) == true && a.ValueKind == JsonValueKind.Object)
                    {
                        argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(a.GetRawText());
                    }
                    contents.Add(new FunctionCallContent(callId, name, argsDict));
                    break;
                }

                case "function_call_output":
                {
                    // 从 JsonExtensionData 中提取函数调用结果
                    var props = item.AdditionalProperties;
                    var callId = props?.TryGetValue("call_id", out var cid) == true ? cid.GetString() : null;
                    var outputText = props?.TryGetValue("output", out var o) == true ? o.GetString() : null;
                    contents.Add(new FunctionResultContent(callId, outputText));
                    break;
                }

                default:
                {
                    // message / text / output_text：遍历 Contents 提取文本内容
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
    //  辅助方法
    // ──────────────────────────────────────────────

    /// <summary>
    /// 从 MAF ChatOptions.AdditionalProperties 中读取会话标识（Topic ID）。
    /// 未设置时返回 null，表示发起新会话（服务端自动创建）。
    /// </summary>
    private string? GetConversationId(Microsoft.Extensions.AI.ChatOptions? options)
    {
        // 优先从当前请求的 AdditionalProperties 读取
        if (options?.AdditionalProperties?.TryGetValue(ConversationIdKey, out var val) == true
            && val is string s && !string.IsNullOrEmpty(s))
            return s;

        // 回退到内部持久化的值
        if (!string.IsNullOrEmpty(_persistedConversationId))
            return _persistedConversationId;

        return null;
    }

    /// <summary>
    /// 将 MAF ChatOptions 转换为 Hermes SDK ResponseOptions。
    /// 映射字段：ModelId, Instructions, 会话ID, MaxOutputTokens, Temperature 及所有 AdditionalProperties 元数据。
    /// </summary>
    private ResponseOptions ToResponseOptions(Microsoft.Extensions.AI.ChatOptions? options)
    {
        return new ResponseOptions
        {
            Model = options?.ModelId ?? "default",
            Instructions = options?.Instructions,
            Conversation = GetConversationId(options),
            MaxOutputTokens = options?.MaxOutputTokens ?? 1024,
            Temperature = options?.Temperature ?? 0.7f,
            Metadata = options?.AdditionalProperties is { } props
                ? props.Where(kv => kv.Value is not null)
                       .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString()!)
                : [],
        };
    }

    /// <summary>
    /// 将 Hermes SDK UsageInfo 映射为 MAF UsageDetails。
    /// </summary>
    private static Microsoft.Extensions.AI.UsageDetails? MapToMafUsage(UsageInfo? usage)
    {
        if (usage is null) return null;

        return new Microsoft.Extensions.AI.UsageDetails
        {
            InputTokenCount = usage.PromptTokens,
            OutputTokenCount = usage.CompletionTokens,
            TotalTokenCount = usage.TotalTokens,
        };
    }

    // ──────────────────────────────────────────────
    //  SSE 事件处理
    // ──────────────────────────────────────────────

    /// <summary>
    /// SSE 事件处理结果值对象。使用工厂方法创建不同状态：
    /// <list type="bullet">
    ///   <item><description>Yield → 产生一个 ChatResponseUpdate 增量</description></item>
    ///   <item><description>Complete → 流结束，终止枚举</description></item>
    ///   <item><description>Error → 流出错，附带错误消息</description></item>
    ///   <item><description>None → 无操作，继续等待下一事件</description></item>
    /// </list>
    /// </summary>
    private readonly struct SseEventResult
    {
        public Microsoft.Extensions.AI.ChatResponseUpdate? Update { get; }
        public bool ShouldBreak { get; }
        public string? ThrowError { get; }

        private SseEventResult(Microsoft.Extensions.AI.ChatResponseUpdate? update, bool shouldBreak, string? throwError)
        {
            Update = update;
            ShouldBreak = shouldBreak;
            ThrowError = throwError;
        }

        public static SseEventResult Yield(Microsoft.Extensions.AI.ChatResponseUpdate update) => new(update, false, null);
        public static SseEventResult Complete() => new(null, true, null);
        public static SseEventResult Error(string message) => new(null, false, message);
        public static SseEventResult None() => default;
    }

    /// <summary>
    /// 解析单条 SSE 数据并返回对应的事件结果。
    ///
    /// 支持的事件类型：
    /// <list type="bullet">
    ///   <item><description>response.output_text.delta — 流式文本增量，逐字返回</description></item>
    ///   <item><description>response.output_item.added — 函数调用项已添加，服务端已执行，客户端跳过</description></item>
    ///   <item><description>response.completed — 响应完成，提取最终文本（从 output[].message.content 中获取）</description></item>
    ///   <item><description>error — 服务端错误</description></item>
    /// </list>
    /// </summary>
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
                        // 文本增量事件：将 delta 文本直接输出
                        var deltaText = eventRoot.GetProperty("delta").GetString();
                        if (!string.IsNullOrEmpty(deltaText))
                        {
                            return SseEventResult.Yield(
                                new Microsoft.Extensions.AI.ChatResponseUpdate(null, new List<Microsoft.Extensions.AI.AIContent> { new Microsoft.Extensions.AI.TextContent(deltaText) }));
                        }

                        return SseEventResult.None();
                    }

                case "response.output_item.added":
                    {
                        // function_call 已由 Hermes 服务端执行，无需在客户端重复调用
                        return SseEventResult.None();
                    }

                case "response.completed":
                    {
                        // 响应完成事件：终止流。
                        // 文本内容已通过 response.output_text.delta 事件逐段 yield，无需重复输出。
                        if (eventRoot.TryGetProperty("response", out var completedResp))
                        {
                            // 写回服务端返回的 Conversation ID 并持久化，确保多轮对话关联
                            if (completedResp.TryGetProperty("conversation", out var convProp))
                            {
                                var convId = convProp.GetString();
                                if (!string.IsNullOrEmpty(convId))
                                {
                                    options ??= new();
                                    options.AdditionalProperties ??= [];
                                    options.AdditionalProperties[ConversationIdKey] = convId;
                                    _persistedConversationId = convId;
                                }
                            }
                        }
                        return SseEventResult.Complete();
                    }

                case "error":
                    {
                        // 服务端错误事件
                        var errorMsg = eventRoot.TryGetProperty("error", out var err)
                            ? err.GetString()
                            : "Unknown Responses API error";
                        return SseEventResult.Error(errorMsg!);
                    }

                default:
                    _logger.LogDebug("跳过未知的 Responses API SSE 事件: {EventType}", eventType);
                    return SseEventResult.None();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "无法解析 Responses API SSE 数据: {Data}", data);
            return SseEventResult.None();
        }
    }
}

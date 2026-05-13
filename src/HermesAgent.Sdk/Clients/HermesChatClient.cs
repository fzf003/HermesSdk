using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 聊天客户端实现，用于与 Hermes Agent 进行聊天交互。
/// 使用场景：应用程序需要集成 AI 聊天功能，如客服机器人、智能助手或对话界面。
/// 支持同步和异步聊天、流式响应等。
/// </summary>
public class HermesChatClient : IHermesChatClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
     private readonly ILogger<HermesChatClient> _logger;
    /// <summary>
    /// 初始化 HermesChatClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    /// <param name="logger">用于记录日志的 ILogger 实例。</param>
    public HermesChatClient(HttpClient httpClient, ILogger<HermesChatClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 简单问答接口实现。
    /// 使用场景：快速提问并获取文本回答，无需复杂配置。
    /// </summary>
    /// <param name="message">用户消息。</param>
    /// <param name="systemPrompt">可选的系统提示，用于指导 AI 行为。</param>
    /// <param name="options">聊天选项，如温度、最大令牌数等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 的文本回答。</returns>
    public async Task<string> AskAsync(string message, string? systemPrompt = null, ChatOptions? options = null, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = options?.Model ?? "default",
            Messages = new List<ChatMessage>
            {
                new("system", systemPrompt ?? ""),
                new("user", message)
            },
            Stream = false,
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            MaxTokens = options?.MaxTokens,
            Stop = options?.Stop
        };

        var response = await ChatAsync(request, ct);
        return response.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
    }

    /// <summary>
    /// 结构化聊天请求实现。
    /// 使用场景：需要发送完整的聊天请求对象，包括消息历史和选项。
    /// </summary>
    /// <param name="request">聊天请求对象。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>聊天响应。</returns>
    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(_jsonOptions, ct);
        return result ?? throw new InvalidOperationException("Invalid chat response");
    }

    /// <summary>
    /// 流式聊天响应实现。
    /// 使用场景：需要实时接收聊天响应片段，如构建聊天界面或处理长响应。
    /// </summary>
    /// <param name="request">聊天请求对象。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步可枚举的聊天流片段。</returns>
    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var streamRequest = request with { Stream = true };
        using var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", streamRequest, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (!line.StartsWith("data: "))
                continue;
            _logger.LogDebug("Streaming: {0}", line);
            var data = line[6..];
            if (data == "[DONE]")
                yield break;
            var chunk = JsonSerializer.Deserialize<ChatStreamChunk>(data, _jsonOptions);
            if (chunk is not null)
                yield return chunk;
        }
    }

    /// <summary>
    /// 基于消息列表的聊天实现。
    /// 使用场景：已有消息历史列表，需要继续对话或处理多轮对话。
    /// </summary>
    /// <param name="messages">消息列表。</param>
    /// <param name="options">聊天选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>聊天响应。</returns>
    public Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = options?.Model ?? "default",
            Messages = messages.ToList(),
            Stream = false,
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            MaxTokens = options?.MaxTokens,
            Stop = options?.Stop
        };

        return ChatAsync(request, ct);
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

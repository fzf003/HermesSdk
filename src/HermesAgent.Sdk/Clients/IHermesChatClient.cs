namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 聊天客户端接口，用于与 Hermes Agent 进行聊天交互。
/// 使用场景：需要与 AI 助手进行对话、问答或多轮对话时使用此接口。
/// 支持简单问答、结构化聊天请求和流式响应。
/// </summary>
public interface IHermesChatClient : IDisposable
{
    /// <summary>
    /// 简单问答接口。
    /// 使用场景：快速提问并获取文本回答，无需复杂配置。
    /// </summary>
    /// <param name="message">用户消息。</param>
    /// <param name="systemPrompt">可选的系统提示，用于指导 AI 行为。</param>
    /// <param name="options">聊天选项，如温度、最大令牌数等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 的文本回答。</returns>
    Task<string> AskAsync(string message, string? systemPrompt = null, ChatOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 结构化聊天请求。
    /// 使用场景：需要发送完整的聊天请求对象，包括消息历史和选项。
    /// </summary>
    /// <param name="request">聊天请求对象。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>聊天响应。</returns>
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 流式聊天响应。
    /// 使用场景：需要实时接收聊天响应片段，如构建聊天界面或处理长响应。
    /// </summary>
    /// <param name="request">聊天请求对象。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步可枚举的聊天流片段。</returns>
    IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 基于消息列表的聊天。
    /// 使用场景：已有消息历史列表，需要继续对话或处理多轮对话。
    /// </summary>
    /// <param name="messages">消息列表。</param>
    /// <param name="options">聊天选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>聊天响应。</returns>
    Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default);
}

using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// 聊天消息模型，表示聊天对话中的一条消息。
/// 使用场景：在聊天请求中传递消息历史，或构建多轮对话。
/// </summary>
public record ChatMessage
{
    /// <summary>
    /// 消息角色，如 "user"、"assistant"、"system"。
    /// 使用场景：标识消息的发送者类型，影响 AI 的响应行为。
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; init; }

    /// <summary>
    /// 消息内容。
    /// 使用场景：包含用户输入或 AI 响应的文本内容。
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; }

    /// <summary>
    /// 可选的消息名称。
    /// 使用场景：在多参与者对话中标识特定参与者。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// 工具调用列表。
    /// 使用场景：当 AI 需要调用外部工具或函数时使用。
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// 创建聊天消息实例。
    /// </summary>
    /// <param name="role">消息角色。</param>
    /// <param name="content">消息内容。</param>
    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

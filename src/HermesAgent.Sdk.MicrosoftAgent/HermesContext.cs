namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// 通过 AsyncLocal 在当前调用链中传递 Hermes Conversation ID。
/// 适配器从上下文中读取 conversation_id，将其传递给 Hermes Server。
/// 所有会话状态由 Server 维护，客户端不跟踪任何会话数据。
/// </summary>
public static class HermesContext
{
    private static readonly AsyncLocal<string?> _conversationId = new();

    /// <summary>在当前上下文中设置 conversation_id。</summary>
    public static void SetConversationId(string id) => _conversationId.Value = id;

    /// <summary>获取当前上下文的 conversation_id。</summary>
    public static string? GetConversationId() => _conversationId.Value;

    /// <summary>清除当前上下文的 conversation_id。</summary>
    public static void Clear() => _conversationId.Value = null;
}

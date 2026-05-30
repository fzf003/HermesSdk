namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// AgentSession 持久化存储接口。
/// 负责存储和加载序列化的 AgentSession JSON。
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// 保存序列化的 AgentSession。
    /// </summary>
    /// <param name="sessionId">应用层会话标识。</param>
    /// <param name="json">agent.SerializeSessionAsync() 产生的 JSON。</param>
    /// <param name="expiration">过期时间，null 表示不过期。</param>
    Task SaveAsync(string sessionId, string json, TimeSpan? expiration = null);

    /// <summary>
    /// 加载序列化的 AgentSession JSON。
    /// </summary>
    /// <param name="sessionId">应用层会话标识。</param>
    /// <returns>序列化的 JSON，不存在或已过期则返回 null。</returns>
    Task<string?> LoadAsync(string sessionId);

    /// <summary>
    /// 删除会话。
    /// </summary>
    /// <param name="sessionId">应用层会话标识。</param>
    Task DeleteAsync(string sessionId);
}

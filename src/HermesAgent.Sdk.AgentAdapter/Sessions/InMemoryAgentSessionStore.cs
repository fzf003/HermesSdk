using System.Collections.Concurrent;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 内存版 AgentSession 持久化存储，适用于单机场景或开发测试。
/// 注意：服务重启后会话会丢失。
/// </summary>
public class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly TimeSpan? _defaultExpiration;

    /// <summary>
    /// 初始化内存会话存储。
    /// </summary>
    /// <param name="defaultExpiration">默认会话过期时间，null 表示不过期。</param>
    public InMemoryAgentSessionStore(TimeSpan? defaultExpiration = null)
    {
        _defaultExpiration = defaultExpiration;
    }

    /// <inheritdoc />
    public Task SaveAsync(string sessionId, string json, TimeSpan? expiration = null)
    {
        var exp = expiration ?? _defaultExpiration;
        _sessions[sessionId] = new SessionEntry
        {
            Json = json,
            ExpiresAt = exp.HasValue ? DateTime.Now.Add(exp.Value) : DateTime.MaxValue
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> LoadAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            if (entry.ExpiresAt > DateTime.Now)
                return Task.FromResult<string?>(entry.Json);

            // 会话已过期，删除
            _sessions.TryRemove(sessionId, out _);
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清理所有过期会话。
    /// </summary>
    /// <returns>清理的会话数量。</returns>
    public int CleanupExpiredSessions()
    {
        var now = DateTime.Now;
        var expiredKeys = _sessions
            .Where(kv => kv.Value.ExpiresAt <= now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _sessions.TryRemove(key, out _);
        }

        return expiredKeys.Count;
    }

    /// <summary>
    /// 获取当前活跃会话数量。
    /// </summary>
    public int ActiveSessionCount => _sessions.Count(kv => kv.Value.ExpiresAt > DateTime.Now);

    private class SessionEntry
    {
        public string Json { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }
}

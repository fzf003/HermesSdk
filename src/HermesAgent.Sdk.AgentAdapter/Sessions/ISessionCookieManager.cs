namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 会话 Cookie 管理器接口，用于在 Web 场景下读写 Cookie。
/// </summary>
public interface ISessionCookieManager
{
    /// <summary>
    /// 设置会话 Cookie。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="expiration">过期时间。</param>
    void SetCookie(string sessionId, TimeSpan? expiration = null);

    /// <summary>
    /// 获取会话 Cookie。
    /// </summary>
    /// <returns>会话标识，如果不存在则返回 null。</returns>
    string? GetCookie();

    /// <summary>
    /// 删除会话 Cookie。
    /// </summary>
    void DeleteCookie();
}

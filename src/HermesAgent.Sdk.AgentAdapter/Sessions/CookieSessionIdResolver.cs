using Microsoft.AspNetCore.Http;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 从 Cookie 中解析 SessionId，适用于 Web 浏览器场景。
/// </summary>
public class CookieSessionIdResolver : ISessionIdResolver
{
    private readonly string _cookieName;

    /// <summary>
    /// 初始化 Cookie SessionId 解析器。
    /// </summary>
    /// <param name="cookieName">Cookie 名称，默认 "ChatSessionId"。</param>
    public CookieSessionIdResolver(string cookieName = "ChatSessionId")
    {
        _cookieName = cookieName;
    }

    /// <inheritdoc />
    public string? Resolve(object? context = null)
    {
        if (context is HttpContext httpContext &&
            httpContext.Request.Cookies.TryGetValue(_cookieName, out var sessionId))
        {
            return sessionId;
        }

        return null;
    }
}

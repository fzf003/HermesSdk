using Microsoft.AspNetCore.Http;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 会话 Cookie 管理器实现，用于 Web 场景下管理会话 Cookie。
/// </summary>
public class SessionCookieManager : ISessionCookieManager
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _cookieName;
    private readonly CookieOptions _cookieOptions;

    /// <summary>
    /// 初始化 Cookie 管理器。
    /// </summary>
    /// <param name="httpContextAccessor">HTTP 上下文访问器。</param>
    /// <param name="cookieName">Cookie 名称，默认 "ChatSessionId"。</param>
    /// <param name="secure">是否仅 HTTPS。</param>
    /// <param name="httpOnly">是否仅 HTTP 访问。</param>
    public SessionCookieManager(
        IHttpContextAccessor httpContextAccessor,
        string cookieName = "ChatSessionId",
        bool secure = true,
        bool httpOnly = true)
    {
        _httpContextAccessor = httpContextAccessor;
        _cookieName = cookieName;
        _cookieOptions = new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };
    }

    /// <inheritdoc />
    public void SetCookie(string sessionId, TimeSpan? expiration = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        var options = new CookieOptions
        {
            HttpOnly = _cookieOptions.HttpOnly,
            Secure = _cookieOptions.Secure,
            SameSite = _cookieOptions.SameSite,
            Path = _cookieOptions.Path,
            Expires = expiration.HasValue
                ? DateTimeOffset.UtcNow.Add(expiration.Value)
                : DateTimeOffset.UtcNow.AddHours(24)
        };

        httpContext.Response.Cookies.Append(_cookieName, sessionId, options);
    }

    /// <inheritdoc />
    public string? GetCookie()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        if (httpContext.Request.Cookies.TryGetValue(_cookieName, out var sessionId))
        {
            return sessionId;
        }

        return null;
    }

    /// <inheritdoc />
    public void DeleteCookie()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        httpContext.Response.Cookies.Delete(_cookieName);
    }
}

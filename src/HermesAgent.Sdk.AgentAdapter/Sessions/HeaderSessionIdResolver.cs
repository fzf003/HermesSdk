using Microsoft.AspNetCore.Http;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 从 HTTP Header 中解析 SessionId，适用于 API 调用、移动端等非 Web 场景。
/// </summary>
public class HeaderSessionIdResolver : ISessionIdResolver
{
    private readonly string _headerName;

    /// <summary>
    /// 初始化 Header SessionId 解析器。
    /// </summary>
    /// <param name="headerName">Header 名称，默认 "X-Session-Id"。</param>
    public HeaderSessionIdResolver(string headerName = "X-Session-Id")
    {
        _headerName = headerName;
    }

    /// <inheritdoc />
    public string? Resolve(object? context = null)
    {
        if (context is HttpContext httpContext &&
            httpContext.Request.Headers.TryGetValue(_headerName, out var sessionId))
        {
            return sessionId.ToString();
        }

        return null;
    }
}

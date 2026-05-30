namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// SessionId 解析器接口，用于从请求中提取 SessionId。
/// 支持多种来源：Cookie、Header、Query 等。
/// </summary>
public interface ISessionIdResolver
{
    /// <summary>
    /// 从请求上下文中解析 SessionId。
    /// </summary>
    /// <param name="context">请求上下文，Web 环境为 HttpContext，非 Web 环境为 null。</param>
    /// <returns>解析到的 SessionId，如果未找到则返回 null。</returns>
    string? Resolve(object? context = null);
}

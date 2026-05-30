namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 组合 SessionId 解析器，按优先级尝试多个解析器。
/// 适用于需要同时支持 Web 和非 Web 场景的情况。
/// </summary>
public class CompositeSessionIdResolver : ISessionIdResolver
{
    private readonly IEnumerable<ISessionIdResolver> _resolvers;

    /// <summary>
    /// 初始化组合解析器。
    /// </summary>
    /// <param name="resolvers">解析器列表，按优先级排序。</param>
    public CompositeSessionIdResolver(IEnumerable<ISessionIdResolver> resolvers)
    {
        _resolvers = resolvers;
    }

    /// <inheritdoc />
    public string? Resolve(object? context = null)
    {
        foreach (var resolver in _resolvers)
        {
            var sessionId = resolver.Resolve(context);
            if (!string.IsNullOrEmpty(sessionId))
            {
                return sessionId;
            }
        }

        return null;
    }
}

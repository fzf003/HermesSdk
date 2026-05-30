using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// AgentSession 持久化存储的 DI 扩展方法。
/// </summary>
public static class SessionStoreExtensions
{
    /// <summary>
    /// 添加内存版 AgentSession 持久化存储。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddAgentSessionStore(
        this IServiceCollection services,
        Action<AgentSessionStoreOptions>? configure = null)
    {
        var options = new AgentSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        return services;
    }

    /// <summary>
    /// 添加内存版 AgentSession 持久化存储（控制台模式）。
    /// SessionId 从本地文件读取/保存，适用于控制台应用、CLI 工具。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddAgentSessionStoreForConsole(
        this IServiceCollection services,
        Action<ConsoleSessionStoreOptions>? configure = null)
    {
        var options = new ConsoleSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        // 控制台模式：从本地文件解析 SessionId
        services.TryAddSingleton<ISessionIdResolver>(sp =>
            new ConsoleSessionIdResolver(options.SessionFile));

        return services;
    }

    /// <summary>
    /// 添加内存版 AgentSession 持久化存储（Web 模式）。
    /// 自动从 Cookie 读取 SessionId，客户端无需手动传递。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddAgentSessionStoreForWeb(
        this IServiceCollection services,
        Action<AgentSessionStoreOptions>? configure = null)
    {
        var options = new AgentSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        // Web 模式：从 Cookie 解析 SessionId
        services.TryAddSingleton<ISessionIdResolver, CookieSessionIdResolver>();

        return services;
    }

    /// <summary>
    /// 添加内存版 AgentSession 持久化存储（混合模式）。
    /// 同时支持 Web（Cookie）和非 Web（Header）场景。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddAgentSessionStoreHybrid(
        this IServiceCollection services,
        Action<AgentSessionStoreOptions>? configure = null)
    {
        var options = new AgentSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        // 混合模式：优先 Cookie，其次 Header
        services.TryAddSingleton<ISessionIdResolver>(sp =>
        {
            var resolvers = new ISessionIdResolver[]
            {
                new CookieSessionIdResolver(),
                new HeaderSessionIdResolver()
            };
            return new CompositeSessionIdResolver(resolvers);
        });

        return services;
    }
}

/// <summary>
/// AgentSession 存储配置选项。
/// </summary>
public class AgentSessionStoreOptions
{
    /// <summary>
    /// 默认会话过期时间，默认 30 分钟。
    /// </summary>
    public TimeSpan? DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// 控制台会话存储配置选项。
/// </summary>
public class ConsoleSessionStoreOptions : AgentSessionStoreOptions
{
    /// <summary>
    /// 会话文件路径，默认 ".hermes-session"。
    /// </summary>
    public string SessionFile { get; set; } = ".hermes-session";
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// SessionId 解析方式。
/// </summary>
public enum SessionMethod
{
    Cookie,
    Header
}

/// <summary>
/// AgentSession 持久化存储的 DI 扩展方法。
/// </summary>
public static class SessionStoreExtensions
{
    /// <summary>
    /// 添加内存版 AgentSession 持久化存储。
    /// </summary>
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
    /// </summary>
    public static IServiceCollection AddAgentSessionStoreForConsole(
        this IServiceCollection services,
        Action<ConsoleSessionStoreOptions>? configure = null)
    {
        var options = new ConsoleSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        services.TryAddSingleton<ISessionIdResolver>(sp =>
            new ConsoleSessionIdResolver(options.SessionFile));

        return services;
    }

    /// <summary>
    /// 添加内存版 AgentSession 持久化存储（Web 模式）。
    /// </summary>
    public static IServiceCollection AddAgentSessionStoreForWeb(
        this IServiceCollection services,
        Action<AgentSessionStoreOptions>? configure = null)
    {
        var options = new AgentSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        services.TryAddSingleton<ISessionIdResolver, CookieSessionIdResolver>();

        return services;
    }

    /// <summary>
    /// 添加内存版 AgentSession 持久化存储（可选模式）。
    /// 通过 options.Method 指定使用 Cookie 还是 Header 解析 SessionId。
    /// </summary>
    public static IServiceCollection AddAgentSessionStoreHybrid(
        this IServiceCollection services,
        Action<AgentSessionStoreOptions>? configure = null)
    {
        var options = new AgentSessionStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IAgentSessionStore>(sp =>
            new InMemoryAgentSessionStore(options.DefaultExpiration));

        // 注册两种 resolver 为 keyed service，供其他地方按 key 获取
        services.AddKeyedSingleton<ISessionIdResolver>(SessionMethod.Cookie, (sp, _) => new CookieSessionIdResolver());
        services.AddKeyedSingleton<ISessionIdResolver>(SessionMethod.Header, (sp, _) => new HeaderSessionIdResolver());

        // 根据配置选择默认 ISessionIdResolver
        services.AddSingleton<ISessionIdResolver>(sp =>
            sp.GetRequiredKeyedService<ISessionIdResolver>(options.AuthMethod));

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

    /// <summary>
    /// SessionId 解析方式，默认 Cookie。
    /// </summary>
    public SessionMethod AuthMethod { get; set; } = SessionMethod.Cookie;
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

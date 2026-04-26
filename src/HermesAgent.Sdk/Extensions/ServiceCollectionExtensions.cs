using HermesAgent.Sdk.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace HermesAgent.Sdk.Extensions;

/// <summary>
/// 依赖注入扩展方法，用于注册 Hermes Agent SDK 服务。
/// 使用场景：在应用程序启动时配置依赖注入容器，注册所有客户端和服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加完整的 Hermes Agent SDK 服务。
    /// 使用场景：应用程序需要使用所有 Hermes Agent 功能时，使用此方法一次性注册所有客户端。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例，用于读取 HermesAgent 配置节。</param>
    /// <returns>服务集合，用于链式调用。</returns>
    /// <exception cref="InvalidOperationException">当缺少 HermesAgent 配置节时抛出。</exception>
    public static IServiceCollection AddHermesAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("HermesAgent").Get<HermesAgentOptions>()
            ?? throw new InvalidOperationException("缺少 HermesAgent 配置节");

        services.Configure<HermesAgentOptions>(configuration.GetSection("HermesAgent"));

        services.AddHttpClient<IHermesChatClient, HermesChatClient>(client =>
        {
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            client.Timeout = options.Timeout;
        });

        services.AddHttpClient<IHermesResponseClient, HermesResponseClient>(client =>
        {
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            client.Timeout = options.Timeout;
        });

        services.AddHttpClient<IHermesRunClient, HermesRunClient>(client =>
        {
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHttpClient<IHermesJobClient, HermesJobClient>(client =>
        {
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            client.Timeout = options.Timeout;
        });

        services.AddHttpClient<IHermesWebhookClient, HermesWebhookClient>(client =>
        {
            client.BaseAddress = new Uri(options.WebhookBaseUrl ?? options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    /// <summary>
    /// 添加 Hermes 聊天客户端服务。
    /// 使用场景：应用程序只需要聊天功能时，使用此方法单独注册聊天客户端。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="apiBaseUrl">API 基础 URL。</param>
    /// <param name="apiKey">API 密钥。</param>
    /// <returns>服务集合，用于链式调用。</returns>
    public static IServiceCollection AddHermesChatClient(
        this IServiceCollection services,
        string apiBaseUrl,
        string apiKey)
    {
        services.AddHttpClient<IHermesChatClient, HermesChatClient>(client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        return services;
    }

}

using HermesAgent.Sdk;
using Microsoft.AspNetCore.Builder;

namespace HermesAgent.Sdk.AspNetCore;

/// <summary>
/// Hermes Webhook 中间件扩展方法。
/// 使用场景：在 ASP.NET Core 应用程序中集成 webhook 接收功能，处理来自 Hermes Agent 的回调事件。
/// </summary>
public static class HermesWebhookMiddlewareExtensions
{
    /// <summary>
    /// 使用 Hermes Webhook 中间件。
    /// 使用场景：在应用程序启动时配置 webhook 端点，用于接收和处理 Hermes Agent 发送的事件通知。
    /// </summary>
    /// <param name="app">应用程序构建器。</param>
    /// <param name="path">webhook 端点路径，默认 "/webhooks/hermes-callback"。</param>
    /// <param name="configure">可选的配置委托，用于自定义接收选项。</param>
    /// <returns>应用程序构建器，用于链式调用。</returns>
    public static IApplicationBuilder UseHermesWebhook(
        this IApplicationBuilder app,
        string path = "/webhooks/hermes-callback",
        Action<HermesWebhookReceiverOptions>? configure = null)
    {
        var options = new HermesWebhookReceiverOptions();
        configure?.Invoke(options);

        app.Map(path, builder =>
        {
            builder.UseMiddleware<HermesWebhookMiddleware>(options);
        });

        return app;
    }
}

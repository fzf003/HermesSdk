using System.Text.Json;
using HermesAgent.Sdk;
using Microsoft.AspNetCore.Http;

namespace HermesAgent.Sdk.AspNetCore;

/// <summary>
/// Hermes Webhook 中间件，用于处理来自 Hermes Agent 的 webhook 回调请求。
/// 使用场景：在 ASP.NET Core 应用程序中自动处理 webhook 事件，如聊天完成、运行状态更新等。
/// 提供事件验证、处理和错误处理功能。
/// </summary>
public class HermesWebhookMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HermesWebhookReceiverOptions _options;

    /// <summary>
    /// 初始化 HermesWebhookMiddleware 实例。
    /// </summary>
    /// <param name="next">下一个请求委托。</param>
    /// <param name="options">webhook 接收选项。</param>
    public HermesWebhookMiddleware(RequestDelegate next, HermesWebhookReceiverOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// 处理 webhook 请求。
    /// 使用场景：自动处理传入的 webhook 请求，验证事件类型并调用用户定义的处理程序。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>异步任务。</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // 检查是否需要 HTTPS
        if (_options.RequireHttps && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("HTTPS required");
            return;
        }

        // 读取请求体
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

        // 构建回调上下文
        var callback = new WebhookCallbackContext
        {
            EventType = context.Request.Headers["X-Event-Type"].ToString(),
            RouteName = context.Request.Headers["X-Route-Name"].ToString(), //context.Request.Path.Value?.Trim('/') ?? string.Empty,
            Input = body,
            Output = string.Empty,
            RawBody = body,
            DeliveryId = context.Request.Headers["Idempotency-Key"].ToString()
        };

        try
        {
            // 检查事件类型是否被允许
            if (_options.AllowedEventTypes?.Any() == true && !_options.AllowedEventTypes.Contains(callback.EventType))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            // 调用完成处理程序
            if (_options.OnCompletion != null)
            {
                await _options.OnCompletion(callback);
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("OK");
        }
        catch (Exception ex)
        {
            // 调用错误处理程序
            if (_options.OnError != null)
            {
                await _options.OnError(ex, callback);
            }
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Webhook processing failed");
        }
    }
}

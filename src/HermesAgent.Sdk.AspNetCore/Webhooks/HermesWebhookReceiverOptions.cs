namespace HermesAgent.Sdk.AspNetCore;

/// <summary>
/// Hermes Webhook 接收选项配置类。
/// 使用场景：在配置 webhook 中间件时自定义接收行为，包括安全验证、事件过滤和错误处理。
/// </summary>
public class HermesWebhookReceiverOptions
{
    /// <summary>
    /// Webhook 签名密钥，用于验证请求的真实性。
    /// 使用场景：设置后，中间件会验证请求签名以防止伪造的 webhook 调用。
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Webhook 完成处理委托。
    /// 使用场景：当接收到有效的 webhook 事件时调用，用于处理业务逻辑。
    /// </summary>
    public Func<HermesAgent.Sdk.WebhookCallbackContext, Task>? OnCompletion { get; set; }

    /// <summary>
    /// 允许的事件类型列表。
    /// 使用场景：限制只处理特定类型的事件，提高安全性。为空时接受所有事件。
    /// </summary>
    public List<string>? AllowedEventTypes { get; set; }

    /// <summary>
    /// 错误处理委托。
    /// 使用场景：当 webhook 处理过程中发生异常时调用，用于记录错误或执行清理操作。
    /// </summary>
    public Func<Exception, HermesAgent.Sdk.WebhookCallbackContext, Task>? OnError { get; set; }

    /// <summary>
    /// 是否要求 HTTPS 连接。
    /// 使用场景：在生产环境中启用以确保 webhook 通信的安全性。
    /// </summary>
    public bool RequireHttps { get; set; } = false;
}

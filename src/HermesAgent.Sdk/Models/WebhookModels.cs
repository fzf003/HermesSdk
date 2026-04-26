namespace HermesAgent.Sdk;

/// <summary>
/// Webhook 发送结果模型。
/// 使用场景：记录 webhook 发送操作的结果，包括成功状态和可能的错误信息。
/// </summary>
public record WebhookSendResult
{
    /// <summary>
    /// 发送状态，如 "accepted"、"error"。
    /// 使用场景：判断 webhook 是否成功发送。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 可选的交付标识符。
    /// 使用场景：跟踪特定的 webhook 交付，用于调试或日志记录。
    /// </summary>
    public string? DeliveryId { get; init; }

    /// <summary>
    /// HTTP 响应状态码。
    /// 使用场景：获取 webhook 端点的实际 HTTP 响应状态。
    /// </summary>
    public int HttpStatusCode { get; init; }

    /// <summary>
    /// 错误消息（如果有）。
    /// 使用场景：当发送失败时提供详细的错误信息。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Webhook 回调上下文模型。
/// 使用场景：处理接收到的 webhook 回调时，封装请求的完整上下文信息。
/// </summary>
public record WebhookCallbackContext
{
    /// <summary>
    /// 事件类型，如 "chat.completed"、"run.finished"。
    /// 使用场景：根据不同事件类型进行不同的处理逻辑。
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Webhook 路由名称。
    /// 使用场景：标识 webhook 端点，用于路由到正确的处理程序。
    /// </summary>
    public string RouteName { get; init; } = string.Empty;

    /// <summary>
    /// 事件输入数据。
    /// 使用场景：包含触发事件的相关输入信息。
    /// </summary>
    public string Input { get; init; } = string.Empty;

    /// <summary>
    /// 事件输出数据。
    /// 使用场景：包含事件处理的结果或输出信息。
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// 可选的交付标识符。
    /// 使用场景：关联原始发送请求和回调处理。
    /// </summary>
    public string? DeliveryId { get; init; }

    /// <summary>
    /// 原始请求体内容。
    /// 使用场景：保留完整的原始数据，用于验证签名或重新处理。
    /// </summary>
    public string RawBody { get; init; } = string.Empty;
}

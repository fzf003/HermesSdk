namespace HermesAgent.Sdk.Configuration;

/// <summary>
/// Hermes Agent SDK 配置选项。
/// 使用场景：通过依赖注入配置 SDK 的行为，如 API 端点、认证信息、超时设置等。
/// 可通过 appsettings.json 或代码配置。
/// </summary>
public class HermesAgentOptions
{
    /// <summary>
    /// Hermes Agent API 基础 URL。
    /// 使用场景：指定 Hermes Agent 服务的主机地址和端口。
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:8642";

    /// <summary>
    /// API 密钥，用于认证请求。
    /// 使用场景：提供有效的 API 密钥以访问受保护的端点。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 默认使用的 AI 模型名称。
    /// 使用场景：当请求中未指定模型时使用的默认模型。
    /// </summary>
    public string DefaultModel { get; set; } = "default";

    /// <summary>
    /// Webhook 接收器基础 URL。
    /// 使用场景：指定接收 webhook 事件的本地服务地址。
    /// </summary>
    public string? WebhookBaseUrl { get; set; } = "http://localhost:8644";

    /// <summary>
    /// Webhook 签名密钥。
    /// 使用场景：验证接收到的 webhook 事件的真实性。
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// 请求超时时间。
    /// 使用场景：设置 HTTP 请求的最大等待时间，避免长时间挂起。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// 最大重试次数。
    /// 使用场景：网络请求失败时的自动重试次数。
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 是否启用请求日志记录。
    /// 使用场景：调试时记录 HTTP 请求和响应，便于排查问题。
    /// </summary>
    public bool EnableRequestLogging { get; set; } = true;
}

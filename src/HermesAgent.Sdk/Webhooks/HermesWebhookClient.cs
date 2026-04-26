using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes Webhook 客户端，用于向注册的 webhook 端点发送事件通知。
/// 使用场景：当 Hermes Agent 完成聊天、运行任务或作业时，通过 webhook 通知外部系统。
/// 支持事件类型如聊天完成、运行状态更新、作业结果等。
/// </summary>
public class HermesWebhookClient : IHermesWebhookClient
{
    private readonly HttpClient _httpClient;


    private readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 初始化 HermesWebhookClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    public HermesWebhookClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 发送结构化数据作为 webhook 负载。
    /// 使用场景：当需要发送复杂对象（如聊天响应、运行结果）时，使用此方法自动序列化为 JSON。
    /// </summary>
    /// <typeparam name="T">负载数据的类型。</typeparam>
    /// <param name="routeName">webhook 路由名称，用于标识目标端点。</param>
    /// <param name="eventType">事件类型，如 "chat.completed"、"run.finished"。</param>
    /// <param name="payload">要发送的负载数据。</param>
    /// <param name="options">可选的 webhook 发送选项，包括签名、头部等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送结果，包含状态和可能的错误信息。</returns>
    public async Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload, WebhookOptions? options = null, CancellationToken ct = default)
    {
        var rawJson = JsonSerializer.Serialize(payload, serializerOptions);
        return await SendCoreAsync(routeName, eventType, rawJson, options, ct);
    }

    /// <summary>
    /// 发送原始 JSON 字符串作为 webhook 负载。
    /// 使用场景：当负载已经是 JSON 字符串，或需要精确控制 JSON 格式时使用。
    /// </summary>
    /// <param name="routeName">webhook 路由名称。</param>
    /// <param name="eventType">事件类型。</param>
    /// <param name="rawJsonPayload">原始 JSON 负载字符串。</param>
    /// <param name="options">可选的 webhook 发送选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送结果。</returns>
    public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload, WebhookOptions? options = null, CancellationToken ct = default)
    {
        return SendCoreAsync(routeName, eventType, rawJsonPayload, options, ct);
    }

    /// <summary>
    /// 发送简单消息，直接交付。
    /// 使用场景：快速发送通知消息，如状态更新或简单事件，无需复杂负载。
    /// 事件类型固定为 "deliver_only"。
    /// </summary>
    /// <param name="routeName">webhook 路由名称。</param>
    /// <param name="message">要发送的消息内容。</param>
    /// <param name="options">可选的 webhook 发送选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送结果。</returns>
    public Task<WebhookSendResult> SendDirectAsync(string routeName, string message, WebhookOptions? options = null, CancellationToken ct = default)
    {
        return SendCoreAsync(routeName, "deliver_only", JsonSerializer.Serialize(new { message }), options, ct);
    }

    /// <summary>
    /// 核心发送逻辑，处理 HTTP 请求的构建和发送。
    /// </summary>
    /// <param name="routeName">webhook 路由名称。</param>
    /// <param name="eventType">事件类型。</param>
    /// <param name="rawJsonPayload">JSON 负载。</param>
    /// <param name="options">发送选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送结果。</returns>
    private async Task<WebhookSendResult> SendCoreAsync(string routeName, string eventType, string rawJsonPayload, WebhookOptions? options, CancellationToken ct)
    {

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/{routeName}");
        request.Headers.Add("X-Event-Type", eventType);
        var jsonpayload = new
        {
            event_type = eventType,
            routeName = routeName,
            payload = rawJsonPayload
        };
        rawJsonPayload = JsonSerializer.Serialize(jsonpayload, serializerOptions);
        request.Content = new StringContent(rawJsonPayload, Encoding.UTF8, "application/json");

        if (options?.Headers != null)
        {
            foreach (var header in options.Headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }
        }

        if (!string.IsNullOrEmpty(options?.SignatureSecret))
        {
            var signature = HermesWebhookSignature.ComputeHmacSha256(rawJsonPayload, options.SignatureSecret);
            request.Headers.Add("X-Webhook-Signature", signature);
        }

        if (!string.IsNullOrEmpty(options?.IdempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", options.IdempotencyKey);
        }

        using var response = await _httpClient.SendAsync(request, ct);

        // Expected response format:
        // {
        //   "status": "accepted",
        //   "route": "dotnet-webhook",
        //   "event": "test",
        //   "delivery_id": "1777028976852"
        // }
        string deliveryId = string.Empty;

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(response.Content.ReadAsStream());
            try
            {
                // 尝试获取 delivery_id 字段，如果没有则默认为空
                if (doc.RootElement.TryGetProperty("delivery_id", out var idElement))
                {
                    deliveryId = idElement.GetString() ?? string.Empty;
                }
            }
            catch
            {
                deliveryId = string.Empty;
            }
        }

        return new WebhookSendResult
        {
            Status = response.IsSuccessStatusCode ? "accepted" : "error",
            HttpStatusCode = (int)response.StatusCode,
            ErrorMessage = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct),
            DeliveryId = deliveryId
        };
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

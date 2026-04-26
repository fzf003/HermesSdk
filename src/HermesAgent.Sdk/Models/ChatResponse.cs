using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// 聊天响应模型。
/// 使用场景：接收 AI 模型的聊天回复，包含生成的文本和元数据。
/// </summary>
public record ChatResponse
{
    /// <summary>
    /// 响应唯一标识符。
    /// 使用场景：跟踪和引用特定的聊天响应。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 对象类型，通常为 "chat.completion"。
    /// 使用场景：标识响应的类型。
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion";

    /// <summary>
    /// 响应创建时间戳（Unix 时间）。
    /// 使用场景：记录响应生成的时间。
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; init; }

    /// <summary>
    /// 使用的模型名称。
    /// 使用场景：确认实际使用的 AI 模型。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 响应选择列表，必填。
    /// 使用场景：包含 AI 生成的不同响应选项，通常只有一个。
    /// </summary>
    [JsonPropertyName("choices")]
    public required List<Choice> Choices { get; init; }

    /// <summary>
    /// 令牌使用统计信息。
    /// 使用场景：监控 API 使用量和成本。
    /// </summary>
    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }
}

/// <summary>
/// 聊天选择模型，表示 AI 生成的一个响应选项。
/// 使用场景：在多个候选响应中选择最佳的一个。
/// </summary>
public record Choice
{
    /// <summary>
    /// 选择索引，通常从 0 开始。
    /// 使用场景：在多个选择中标识位置。
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>
    /// 生成的消息，必填。
    /// 使用场景：包含 AI 的实际回复内容。
    /// </summary>
    [JsonPropertyName("message")]
    public required ChatMessage Message { get; init; }

    /// <summary>
    /// 完成原因，如 "stop"、"length"、"content_filter"。
    /// 使用场景：了解响应为什么停止生成。
    /// </summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

/// <summary>
/// 令牌使用统计信息模型。
/// 使用场景：计算 API 调用成本和监控资源使用。
/// </summary>
public record UsageInfo
{
    /// <summary>
    /// 提示令牌数（输入）。
    /// 使用场景：计算输入文本的令牌消耗。
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    /// <summary>
    /// 完成令牌数（输出）。
    /// 使用场景：计算生成文本的令牌消耗。
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    /// <summary>
    /// 总令牌数。
    /// 使用场景：计算整个请求的总令牌消耗。
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

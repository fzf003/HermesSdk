using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// 聊天流式响应块模型。
/// 使用场景：处理流式聊天响应时使用，每个块包含部分生成的文本。
/// </summary>
public record ChatStreamChunk
{
    /// <summary>
    /// 响应唯一标识符。
    /// 使用场景：关联同一个流式响应的所有块。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 对象类型，通常为 "chat.completion.chunk"。
    /// 使用场景：标识这是流式响应的数据块。
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion.chunk";

    /// <summary>
    /// 块创建时间戳（Unix 时间）。
    /// 使用场景：记录每个数据块的时间顺序。
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; init; }

    /// <summary>
    /// 使用的模型名称。
    /// 使用场景：确认流式响应使用的 AI 模型。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 流式选择列表。
    /// 使用场景：包含当前块的增量内容。
    /// </summary>
    [JsonPropertyName("choices")]
    public List<StreamChoice>? Choices { get; init; }

    /// <summary>
    /// 令牌使用统计信息（仅在最后一块中出现）。
    /// 使用场景：在流式响应结束时获取完整的使用统计。
    /// </summary>
    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }
}

/// <summary>
/// 流式选择模型，表示流式响应中的增量内容。
/// 使用场景：处理实时生成的文本片段。
/// </summary>
public record StreamChoice
{
    /// <summary>
    /// 选择索引，通常为 0。
    /// 使用场景：在多个并行流中标识位置。
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>
    /// 增量内容，必填。
    /// 使用场景：包含本次块新增的文本或角色信息。
    /// </summary>
    [JsonPropertyName("delta")]
    public required DeltaContent Delta { get; init; }

    /// <summary>
    /// 完成原因（仅在最后一块中出现）。
    /// 使用场景：标识流式响应结束的原因。
    /// </summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

/// <summary>
/// 增量内容模型，表示流式响应中的变化部分。
/// 使用场景：累积增量内容以构建完整的响应。
/// </summary>
public record DeltaContent
{
    /// <summary>
    /// 消息角色（通常只在第一块出现）。
    /// 使用场景：标识 AI 助手的角色。
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>
    /// 新增的文本内容。
    /// 使用场景：追加到之前的文本以形成完整回复。
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

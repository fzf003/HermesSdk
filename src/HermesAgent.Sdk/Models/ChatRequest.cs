using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// 聊天请求模型。
/// 使用场景：向 AI 模型发送聊天请求时使用，包含消息历史和生成参数。
/// </summary>
public record ChatRequest
{
    /// <summary>
    /// 要使用的 AI 模型名称，默认 "default"。
    /// 使用场景：指定特定的 AI 模型进行聊天。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = "default";

    /// <summary>
    /// 聊天消息列表，必填。
    /// 使用场景：提供对话历史，包括系统提示、用户消息和助手回复。
    /// </summary>
    [JsonPropertyName("messages")]
    public required List<ChatMessage> Messages { get; init; }

    /// <summary>
    /// 是否启用流式响应，默认 false。
    /// 使用场景：设置为 true 时，响应将以流式方式返回，便于实时显示。
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = false;

    /// <summary>
    /// 温度参数，控制响应的随机性 (0-2)。
    /// 使用场景：较低值产生更确定性响应，较高值产生更多样化响应。
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    /// <summary>
    /// Top-p 采样参数 (0-1)。
    /// 使用场景：控制响应多样性的另一种方式，与温度互补。
    /// </summary>
    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    /// <summary>
    /// 最大生成令牌数。
    /// 使用场景：限制响应长度，避免过长的输出。
    /// </summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 停止序列列表。
    /// 使用场景：指定某些字符串出现时停止生成。
    /// </summary>
    [JsonPropertyName("stop")]
    public List<string>? Stop { get; init; }

    /// <summary>
    /// 频率惩罚参数，减少重复词语 (-2.0 到 2.0)。
    /// 使用场景：降低常用词语的重复出现频率。
    /// </summary>
    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; init; }

    /// <summary>
    /// 存在惩罚参数，鼓励讨论新话题 (-2.0 到 2.0)。
    /// 使用场景：降低已讨论话题的重复出现频率。
    /// </summary>
    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// 元数据字典。
    /// 使用场景：附加自定义信息，如请求标识符、用户上下文等。
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// MAF 适配器扩展字段（Partial Class）。
/// 这些字段是 MAF <c>ChatOptions</c> 映射所需，Hermes 核心 API 原生不含。
/// 文件后缀 <c>.Maf</c> 标记使用场景，避免污染核心模型。
/// </summary>
public partial record ResponseRequest
{
    /// <summary>
    /// 是否启用流式响应。
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>
    /// 频率惩罚系数。
    /// </summary>
    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; init; }

    /// <summary>
    /// 存在惩罚系数。
    /// </summary>
    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Top-P 采样参数。
    /// </summary>
    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    /// <summary>
    /// 停止序列列表。
    /// </summary>
    [JsonPropertyName("stop")]
    public List<string>? StopSequences { get; init; }
}

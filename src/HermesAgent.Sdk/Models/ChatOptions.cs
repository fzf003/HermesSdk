namespace HermesAgent.Sdk;

/// <summary>
/// 聊天选项，用于控制聊天请求的行为和参数。
/// 使用场景：在发起聊天请求时指定模型参数、生成控制等选项。
/// </summary>
public record ChatOptions
{
    /// <summary>
    /// 要使用的 AI 模型名称。
    /// 使用场景：指定特定的模型，如 GPT-4、Claude 等。
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 温度参数，控制响应的随机性 (0-2)。
    /// 使用场景：较低值产生更确定性响应，较高值产生更多样化响应。
    /// </summary>
    public float? Temperature { get; init; }

    /// <summary>
    /// 最大生成令牌数。
    /// 使用场景：限制响应长度，避免过长的输出。
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Top-p 采样参数 (0-1)。
    /// 使用场景：控制响应多样性的另一种方式，与温度互补。
    /// </summary>
    public float? TopP { get; init; }

    /// <summary>
    /// 停止序列列表。
    /// 使用场景：指定某些字符串出现时停止生成，如特定的结束标记。
    /// </summary>
    public List<string>? Stop { get; init; }

    /// <summary>
    /// 请求超时时间。
    /// 使用场景：设置请求的最大等待时间。
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}

using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

/// <summary>
/// 作业创建请求模型。
/// 使用场景：创建新的定时或周期性 AI 作业时使用，定义作业的基本信息和执行参数。
/// </summary>
public record JobCreateRequest
{
    /// <summary>
    /// 作业名称，必填。
    /// 使用场景：为作业提供一个易于识别的名称。
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// 作业执行的提示或指令，必填。
    /// 使用场景：定义 AI 作业需要执行的具体任务或指令。
    /// </summary>
    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    /// <summary>
    /// 作业调度表达式，必填。
    /// 使用场景：定义作业的执行时间表，如 cron 表达式或时间间隔。
    /// </summary>
    [JsonPropertyName("schedule")]
    public required string Schedule { get; init; }

    /// <summary>
    /// 要使用的 AI 模型名称。
    /// 使用场景：指定作业使用的特定 AI 模型。
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// 作业所需的技能列表。
    /// 使用场景：指定作业执行需要调用的外部技能或工具。
    /// </summary>
    [JsonPropertyName("skills")]
    public List<string>? Skills { get; init; }

    /// <summary>
    /// 作业结果交付方式。
    /// 使用场景：指定作业完成后的结果如何处理，如发送到 webhook 或存储。
    /// </summary>
    [JsonPropertyName("deliver")]
    public string? Deliver { get; init; }

    /// <summary>
    /// 交付的额外参数。
    /// 使用场景：为交付方式提供额外的配置参数。
    /// </summary>
    [JsonPropertyName("deliver_extra")]
    public Dictionary<string, string>? DeliverExtra { get; init; }

    /// <summary>
    /// 作业是否启用，默认 true。
    /// 使用场景：控制作业是否立即开始执行。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// 作业更新请求模型。
/// 使用场景：修改现有作业的配置时使用，所有字段都是可选的。
/// </summary>
public record JobUpdateRequest
{
    /// <summary>
    /// 更新的提示或指令。
    /// 使用场景：修改作业执行的任务内容。
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>
    /// 更新的调度表达式。
    /// 使用场景：修改作业的执行时间表。
    /// </summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>
    /// 更新的 AI 模型名称。
    /// 使用场景：切换作业使用的 AI 模型。
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// 更新的技能列表。
    /// 使用场景：修改作业所需的外部技能。
    /// </summary>
    [JsonPropertyName("skills")]
    public List<string>? Skills { get; init; }

    /// <summary>
    /// 更新的交付方式。
    /// 使用场景：修改作业结果的处理方式。
    /// </summary>
    [JsonPropertyName("deliver")]
    public string? Deliver { get; init; }

    /// <summary>
    /// 作业是否启用。
    /// 使用场景：启用或禁用作业执行。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}

/// <summary>
/// 作业摘要信息模型。
/// 使用场景：列出作业时使用，包含作业的基本状态信息。
/// </summary>
public record JobSummary
{
    /// <summary>
    /// 作业唯一标识符。
    /// 使用场景：用于引用和操作特定作业。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 作业名称。
    /// 使用场景：显示作业的易读名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 作业当前状态，如 "active"、"paused"、"error"。
    /// 使用场景：了解作业的运行状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 作业调度表达式。
    /// 使用场景：查看作业的执行计划。
    /// </summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>
    /// 最后一次执行时间。
    /// 使用场景：监控作业的执行历史。
    /// </summary>
    [JsonPropertyName("last_run")]
    public string? LastRun { get; init; }

    /// <summary>
    /// 下次计划执行时间。
    /// 使用场景：预测作业的下次运行时间。
    /// </summary>
    [JsonPropertyName("next_run")]
    public string? NextRun { get; init; }

    /// <summary>
    /// 作业是否启用。
    /// 使用场景：检查作业是否处于活动状态。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

/// <summary>
/// 作业详细信息模型，继承自 JobSummary。
/// 使用场景：获取作业的完整配置信息时使用。
/// </summary>
public record JobDetail : JobSummary
{
    /// <summary>
    /// 作业执行的提示或指令。
    /// 使用场景：查看作业的具体任务内容。
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>
    /// 使用的 AI 模型名称。
    /// 使用场景：了解作业使用的 AI 模型。
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// 作业所需的技能列表。
    /// 使用场景：查看作业依赖的外部技能。
    /// </summary>
    [JsonPropertyName("skills")]
    public List<string>? Skills { get; init; }

    /// <summary>
    /// 作业结果交付方式。
    /// 使用场景：了解作业结果的处理方式。
    /// </summary>
    [JsonPropertyName("deliver")]
    public string? Deliver { get; init; }
}

/// <summary>
/// 作业运行结果模型。
/// 使用场景：作业执行完成后返回的结果信息。
/// </summary>
public record JobRunResult
{
    /// <summary>
    /// 作业标识符。
    /// 使用场景：标识结果所属的作业。
    /// </summary>
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// 执行状态，如 "success"、"failed"。
    /// 使用场景：判断作业执行是否成功。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 作业执行输出结果。
    /// 使用场景：获取作业生成的实际输出内容。
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>
    /// 执行持续时间（毫秒）。
    /// 使用场景：监控作业执行性能和时间消耗。
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public int? DurationMs { get; init; }
}

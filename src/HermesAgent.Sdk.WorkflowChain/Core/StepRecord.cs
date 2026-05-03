namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤执行状态 — 每一步的生命周期状态机。
/// </summary>
public enum StepStatus
{
    /// <summary>尚未启动</summary>
    Pending,

    /// <summary>AgentStep: Webhook 已发出，等待回调</summary>
    Dispatched,

    /// <summary>CodeStep/DelayStep: 正在同步执行</summary>
    Running,

    /// <summary>成功完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>重启恢复中，等待旧回调或超时重发</summary>
    Recovering,
}

/// <summary>
/// 步骤执行档案 — 追踪每一步的执行过程。
///
/// 排查场景：
///   - instance.StepRecords 中 Pending 的就是未启动的步骤
///   - Dispatched 的就是发了 webhook 还没收到回调的（卡住了）
///   - Failed 的看 ErrorMessage / FullStackTrace
///   - InputSnapshot 看当时发了什么给 Agent
///   - OutputSnapshot 看 Agent 回了什么
///
/// 设计原则：纯 POCO，无接口，无事件，可自然序列化
/// </summary>
public class StepRecord
{
    /// <summary>步骤标识</summary>
    public string StepId { get; init; } = "";

    /// <summary>步骤类型：Agent / Code / Delay / Custom</summary>
    public string StepType { get; init; } = "";

    /// <summary>当前状态</summary>
    public StepStatus Status { get; set; }

    /// <summary>开始执行时间（Dispatched 或 Running 时记录）</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>执行耗时（Completed / Failed 后计算）</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>错误简述（异常类型 + 主消息）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>错误详情</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>由哪个步骤触发（用于并行排查：谁派我出来的？）</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>触发了哪些后续步骤（拓扑追溯用）</summary>
    public List<string>? TriggeredSteps { get; set; }

    /// <summary>
    /// 步骤输入快照。
    /// AgentStep: webhook payload（含 prompt）
    /// CodeStep: 执行时的 context 关键字段快照
    /// </summary>
    public string? InputSnapshot { get; set; }

    /// <summary>
    /// 步骤输出快照。
    /// AgentStep: Agent 原始回复 body
    /// CodeStep: ExecuteAsync 返回值
    /// </summary>
    public string? OutputSnapshot { get; set; }

    /// <summary>完整异常堆栈</summary>
    public string? FullStackTrace { get; set; }
}

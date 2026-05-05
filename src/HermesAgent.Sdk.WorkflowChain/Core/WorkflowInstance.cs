namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流运行时实例 — 追踪一个工作流的完整生命周期。
/// </summary>
public class WorkflowInstance
{
    public WorkflowContext Context { get; init; } = null!;
    public string EntryStepId { get; init; } = "";
    public string? WorkflowName { get; set; }
    public string Status { get; set; } = "running"; // running | timed-out | completed | failed
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>最后一次心跳时间（用于检测僵尸实例，Phase 2 心跳检测使用）</summary>
    internal DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>线程同步锁 — Engine 内部用于保护 StepRecords / InFlightStepIds 并发访问</summary>
    internal readonly object SyncLock = new();

    /// <summary>步骤执行档案 — 有序追踪列表</summary>
    internal List<StepRecord> StepRecords { get; } = new();

    /// <summary>已发出 Webhook 但未收到回调的 Agent 步骤 ID 集合</summary>
    internal HashSet<string> InFlightStepIds { get; } = new();

    /// <summary>获取只读步骤记录（排序后）</summary>
    public IReadOnlyList<StepRecord> GetStepRecords()
        => StepRecords
            .OrderBy(r => r.StartedAt == default ? DateTime.MaxValue : r.StartedAt)
            .ThenBy(r => r.StepId)
            .ToList()
            .AsReadOnly();
}

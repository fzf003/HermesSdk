namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流检查点 — 可序列化的完整快照。
/// 用于持久化和重启恢复。
/// </summary>
public class WorkflowCheckpoint
{
    /// <summary>工作流实例唯一标识</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>入口步骤 ID</summary>
    public string EntryStepId { get; set; } = "";

    /// <summary>工作流状态：running | completed | failed</summary>
    public string Status { get; set; } = "running";

    /// <summary>创建时间</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>最后一次心跳时间</summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>工作流启动时的输入参数</summary>
    public Dictionary<string, string?> InitialInput { get; set; } = new();

    /// <summary>各步骤的输出结果集 stepId → output（字符串化）</summary>
    public Dictionary<string, string?> StepOutputs { get; set; } = new();

    /// <summary>自定义共享数据（字符串化）</summary>
    public Dictionary<string, string?> Data { get; set; } = new();

    /// <summary>是否应继续执行</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>当前正在等待回调的步骤 ID 集合</summary>
    public List<string> PendingStepIds { get; set; } = new();

    /// <summary>已发出 Webhook 但尚未收到回调的 Agent 步骤 ID 集合</summary>
    public List<string> InFlightStepIds { get; set; } = new();

    /// <summary>所有步骤的执行档案</summary>
    public List<StepRecord> StepRecords { get; set; } = new();

    /// <summary>从 WorkflowInstance 创建检查点</summary>
    public static WorkflowCheckpoint FromInstance(WorkflowInstance instance)
    {
        var ctx = instance.Context;

        return new WorkflowCheckpoint
        {
            InstanceId = ctx.InstanceId,
            EntryStepId = instance.EntryStepId,
            Status = instance.Status,
            StartedAt = instance.StartedAt,
            CompletedAt = instance.CompletedAt,
            LastHeartbeat = DateTime.UtcNow,

            InitialInput = ctx.InitialInput.ToDictionary(
                kv => kv.Key, kv => kv.Value?.ToString()),
            StepOutputs = ctx.StepOutputs.ToDictionary(
                kv => kv.Key, kv => kv.Value?.ToString()),
            Data = ctx.Data.ToDictionary(
                kv => kv.Key, kv => kv.Value?.ToString()),
            IsRunning = ctx.IsRunning,

            PendingStepIds = ctx.PendingStepIds.ToList(),
            InFlightStepIds = instance.InFlightStepIds.ToList(),

            StepRecords = instance.StepRecords.Select(r => new StepRecord
            {
                StepId = r.StepId,
                StepType = r.StepType,
                Status = r.Status,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Duration = r.Duration,
                ErrorMessage = r.ErrorMessage,
                ErrorDetail = r.ErrorDetail,
                FullStackTrace = r.FullStackTrace,
                TriggeredBy = r.TriggeredBy,
                TriggeredSteps = r.TriggeredSteps?.ToList(),
                InputSnapshot = r.InputSnapshot,
                OutputSnapshot = r.OutputSnapshot,
            }).ToList(),
        };
    }

    /// <summary>重建 WorkflowInstance</summary>
    public WorkflowInstance ToInstance()
    {
        var ctx = new WorkflowContext
        {
            InstanceId = InstanceId,
            InitialInput = InitialInput.ToDictionary(
                kv => kv.Key, kv => (object?)kv.Value),
            IsRunning = IsRunning,
        };

        foreach (var (key, value) in StepOutputs)
            ctx.StepOutputs[key] = value;
        foreach (var (key, value) in Data)
            ctx.Data[key] = value;
        foreach (var id in PendingStepIds)
            ctx.PendingStepIds.Add(id);

        var instance = new WorkflowInstance
        {
            Context = ctx,
            EntryStepId = EntryStepId,
            Status = Status,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
        };

        foreach (var r in StepRecords)
        {
            instance.StepRecords.Add(new StepRecord
            {
                StepId = r.StepId,
                StepType = r.StepType,
                Status = r.Status,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Duration = r.Duration,
                ErrorMessage = r.ErrorMessage,
                ErrorDetail = r.ErrorDetail,
                FullStackTrace = r.FullStackTrace,
                TriggeredBy = r.TriggeredBy,
                TriggeredSteps = r.TriggeredSteps?.ToList(),
                InputSnapshot = r.InputSnapshot,
                OutputSnapshot = r.OutputSnapshot,
            });
        }

        foreach (var id in InFlightStepIds)
            instance.InFlightStepIds.Add(id);

        return instance;
    }
}

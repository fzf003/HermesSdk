namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流上下文 — 沿职责链传递的共享状态。
/// 各 Handler 通过它读写数据、控制流程。
/// </summary>
public class WorkflowContext
{
    /// <summary>工作流实例唯一标识（每次执行唯一）</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>所属工作流定义名称（与 AddWorkflow().WithName() / YAML name 一致）</summary>
    public string? WorkflowName { get; set; }

    /// <summary>所属工作流定义 ID（WorkflowDefinition.Id，与 YAML / C# 注册的 workflow 定义一致）</summary>
    public string? WorkflowId { get; set; }

    /// <summary>工作流启动时的输入参数</summary>
    public Dictionary<string, object?> InitialInput { get; init; } = new();

    /// <summary>各步骤的输出结果集 stepId → output</summary>
    public Dictionary<string, object?> StepOutputs { get; } = new();

    /// <summary>自定义共享数据</summary>
    public Dictionary<string, object?> Data { get; } = new();

    /// <summary>当前是否应继续执行</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>全局错误信息</summary>
    public Exception? Error { get; set; }

    /// <summary>线程同步锁 — Engine 内部用于保护并发访问</summary>
    internal readonly object SyncLock = new();

    /// <summary>当前正在等待回调的步骤 ID 集合（用于并行 join）</summary>
    internal HashSet<string> PendingStepIds { get; } = new();

    /// <summary>获取指定步骤的输出</summary>
    public T? GetOutput<T>(string stepId) where T : notnull
    {
        if (StepOutputs.TryGetValue(stepId, out var val) && val is T t)
            return t;
        return default;
    }

    /// <summary>设置自定义数据</summary>
    public void SetData(string key, object? value) => Data[key] = value;

    /// <summary>获取自定义数据</summary>
    public T? GetData<T>(string key) where T : notnull
    {
        if (Data.TryGetValue(key, out var val) && val is T t)
            return t;
        return default;
    }
}

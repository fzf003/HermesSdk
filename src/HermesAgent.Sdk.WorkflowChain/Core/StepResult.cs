namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤执行结果 — Handler 返回给调度器的指令。
/// </summary>
public class StepResult
{
    /// <summary>步骤 ID</summary>
    public string StepId { get; init; } = "";

    /// <summary>是否执行成功</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>步骤输出（Agent 回复、CodeStep 返回值等）</summary>
    public object? Output { get; set; }

    /// <summary>异常信息</summary>
    public Exception? Error { get; set; }

    /// <summary>步骤执行耗时</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 该步骤触发的下一步步骤 ID 列表。
    /// 多个 = 并行执行下一步。
    /// </summary>
    public List<string>? NextStepIds { get; set; }

    /// <summary>是否等待所有并行步骤完成后才继续</summary>
    public bool WaitForParallelCompletion { get; set; }

    /// <summary>
    /// ParallelJoin 模式下，所有并行子步骤完成后汇合到的下游步骤 ID。
    /// 非 ParallelJoin 时为 null。
    /// </summary>
    public string? JoinDownstreamStepId { get; set; }
}

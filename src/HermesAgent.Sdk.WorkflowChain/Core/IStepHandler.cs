namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流步骤处理器接口 — 每个 Handler 处理一个步骤，通过返回值告诉调度器下一步做什么。
/// 类比职责链模式中的 IHandler。
/// </summary>
public interface IStepHandler
{
    /// <summary>步骤唯一标识</summary>
    string StepId { get; }

    /// <summary>
    /// 执行本步骤。框架保证：所有依赖步骤都完成后才调用此方法。
    /// </summary>
    Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct);
}

/// <summary>
/// 步骤处理器抽象基类 — 提供 Sequential / Parallel / ParallelJoin / Complete / Failed 工厂方法。
/// </summary>
public abstract class StepHandlerBase : IStepHandler
{
    public abstract string StepId { get; }
    public abstract Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct);

    /// <summary>创建一个序列步骤结果（下一步只有一个）</summary>
    protected static StepResult Sequential(string nextStepId, object? output = null)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            Output = output,
            NextStepIds = new List<string> { nextStepId },
        };

    /// <summary>创建一个并行步骤结果（下一步有多个，等全部完成）</summary>
    protected static StepResult Parallel(params string[] nextStepIds)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            NextStepIds = nextStepIds.ToList(),
            WaitForParallelCompletion = true,
        };

    /// <summary>
    /// 声明式并行汇合：等所有子步骤完成后自动推进到下游步骤。
    /// Engine 内部通过 CountDown 自动计数，Handler 无需手动检查并行伙伴状态。
    /// </summary>
    /// <param name="downstreamStepId">所有子步骤完成后汇合到的下游步骤 ID</param>
    /// <param name="childStepIds">并行子步骤 ID 列表</param>
    protected static StepResult ParallelJoin(string downstreamStepId, params string[] childStepIds)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            NextStepIds = childStepIds.ToList(),
            WaitForParallelCompletion = true,
            JoinDownstreamStepId = downstreamStepId,
        };

    /// <summary>工作流完成（无下一步）</summary>
    protected static StepResult Complete(object? output = null)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            Output = output,
        };

    /// <summary>步骤失败</summary>
    protected static StepResult Failed(Exception ex)
        => new()
        {
            StepId = "",
            IsSuccess = false,
            Error = ex,
        };
}

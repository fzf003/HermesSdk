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

    /// <summary>
    /// 步骤级心跳扩展时长。当返回 null 时使用全局心跳阈值；
    /// 当返回具体 TimeSpan 时，心跳检测的有效阈值取 max(全局阈值, 此值)。
    /// 适用于人工审批等长时间等待场景。
    /// </summary>
    public virtual TimeSpan? HeartbeatExtension => null;

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

    /// <summary>创建一个并行步骤结果（任一完成即推进，其余结果被跳过）</summary>
    protected static StepResult ParallelAny(params string[] nextStepIds)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            NextStepIds = nextStepIds.ToList(),
            WaitForParallelCompletion = true,
            WaitMode = ParallelWaitMode.Any,
        };

    /// <summary>
    /// 声明式并行汇合（任一完成模式）：任一子步骤完成后立即推进到下游步骤。
    /// 其余子步骤的结果将被幂等跳过。
    /// </summary>
    protected static StepResult ParallelJoinAny(string downstreamStepId, params string[] childStepIds)
        => new()
        {
            StepId = "",
            IsSuccess = true,
            NextStepIds = childStepIds.ToList(),
            WaitForParallelCompletion = true,
            JoinDownstreamStepId = downstreamStepId,
            WaitMode = ParallelWaitMode.Any,
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

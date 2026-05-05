using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 错误处理器 - 根据错误策略处理步骤失败。
/// </summary>
public class ErrorHandler
{
    private readonly ILogger<ErrorHandler> _logger;
    private readonly IWorkflowStateStore _stateStore;

    /// <summary>
    /// 创建错误处理器实例。
    /// </summary>
    public ErrorHandler(ILogger<ErrorHandler> logger, IWorkflowStateStore stateStore)
    {
        _logger = logger;
        _stateStore = stateStore;
    }

    /// <summary>
    /// 处理步骤错误。
    /// </summary>
    public async Task HandleErrorAsync(
        WorkflowInstance instance,
        StepRecord record,
        Exception error,
        ErrorPolicy policy,
        CancellationToken ct)
    {
        _logger.LogError(error,
            "步骤 {StepId} 执行失败,策略: {Policy}",
            record.StepId, policy);

        record.Status = StepStatus.Failed;
        record.ErrorMessage = error.Message;
        record.ErrorDetail = error.ToString();
        record.FullStackTrace = error.StackTrace;
        record.CompletedAt = DateTime.UtcNow;
        record.Duration = record.CompletedAt - record.StartedAt;

        switch (policy)
        {
            case ErrorPolicy.FailFast:
                await HandleFailFastAsync(instance, record, ct);
                break;
            case ErrorPolicy.ContinueOnError:
                await HandleContinueOnErrorAsync(instance, record, ct);
                break;
            case ErrorPolicy.SkipFailedBranch:
                await HandleSkipFailedBranchAsync(instance, record, ct);
                break;
        }
    }

    private async Task HandleFailFastAsync(WorkflowInstance instance, StepRecord record, CancellationToken ct)
    {
        instance.Context.IsRunning = false;
        instance.Status = "failed";

        // 标记所有未执行步骤为Failed
        foreach (var pendingRecord in instance.StepRecords.Where(r => r.Status == StepStatus.Pending))
        {
            pendingRecord.Status = StepStatus.Failed;
            pendingRecord.ErrorMessage = $"因步骤 {record.StepId} 失败而跳过";
        }

        await SaveAsync(instance, ct);

        _logger.LogInformation(
            "工作流 {InstanceId} 因步骤 {StepId} 失败而终止",
            instance.Context.InstanceId, record.StepId);
    }

    private async Task HandleContinueOnErrorAsync(WorkflowInstance instance, StepRecord record, CancellationToken ct)
    {
        // 将错误信息注入上下文
        instance.Context.Data[$"error_{record.StepId}"] = new
        {
            StepId = record.StepId,
            Error = record.ErrorMessage,
            Timestamp = record.CompletedAt
        };

        await SaveAsync(instance, ct);

        _logger.LogInformation(
            "步骤 {StepId} 失败但工作流继续执行",
            record.StepId);
    }

    [Obsolete("由Engine直接处理SkipFailedBranch，将在下个版本移除")]
    private async Task HandleSkipFailedBranchAsync(WorkflowInstance instance, StepRecord record, CancellationToken ct)
    {
        await SaveAsync(instance, ct);

        _logger.LogInformation(
            "并行分支 {StepId} 失败被跳过",
            record.StepId);
    }

    private async Task SaveAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var checkpoint = WorkflowCheckpoint.FromInstance(instance);
        await _stateStore.SaveAsync(checkpoint, ct);
    }
}

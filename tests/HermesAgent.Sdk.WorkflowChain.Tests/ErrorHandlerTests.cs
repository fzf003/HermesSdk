using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class ErrorHandlerTests
{
    private readonly ErrorHandler _handler;
    private readonly InMemoryStateStore _store;

    public ErrorHandlerTests()
    {
        _store = new InMemoryStateStore();
        _handler = new ErrorHandler(NullLogger<ErrorHandler>.Instance, _store);
    }

    // ═══════════════════════════════════════════
    // FailFast 策略
    // ═══════════════════════════════════════════

    [Fact]
    public async Task FailFast_TerminatesWorkflow()
    {
        // Arrange
        var instance = CreateInstance("wf-1");
        var record = CreateRecord("step-1");

        // Act
        await _handler.HandleErrorAsync(instance, record, new Exception("boom"), ErrorPolicy.FailFast, CancellationToken.None);

        // Assert
        Assert.False(instance.Context.IsRunning);
        Assert.Equal("failed", instance.Status);
        Assert.Equal(StepStatus.Failed, record.Status);
        Assert.Equal("boom", record.ErrorMessage);
    }

    [Fact]
    public async Task FailFast_MarksPendingStepsAsFailed()
    {
        // Arrange
        var instance = CreateInstance("wf-1");
        var failedRecord = CreateRecord("step-1");
        var pendingRecord = CreateRecord("step-2");
        pendingRecord.Status = StepStatus.Pending;
        instance.StepRecords.Add(pendingRecord);

        // Act
        await _handler.HandleErrorAsync(instance, failedRecord, new Exception("boom"), ErrorPolicy.FailFast, CancellationToken.None);

        // Assert
        Assert.Equal(StepStatus.Failed, pendingRecord.Status);
        Assert.Contains("跳过", pendingRecord.ErrorMessage);
    }

    // ═══════════════════════════════════════════
    // ContinueOnError 策略
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ContinueOnError_LogsAndContinues()
    {
        // Arrange
        var instance = CreateInstance("wf-2");
        var record = CreateRecord("step-1");

        // Act
        await _handler.HandleErrorAsync(instance, record, new Exception("partial fail"), ErrorPolicy.ContinueOnError, CancellationToken.None);

        // Assert
        Assert.True(instance.Context.IsRunning); // should NOT stop
        Assert.Equal(StepStatus.Failed, record.Status);
        Assert.NotNull(instance.Context.Data.ContainsKey("error_step-1"));
    }

    // ═══════════════════════════════════════════
    // SkipFailedBranch 策略
    // ═══════════════════════════════════════════

    [Fact]
    public async Task SkipFailedBranch_DoesNotTerminateWorkflow()
    {
        // Arrange
        var instance = CreateInstance("wf-3");
        var record = CreateRecord("branch-1");

        // Act
        await _handler.HandleErrorAsync(instance, record, new Exception("branch fail"), ErrorPolicy.SkipFailedBranch, CancellationToken.None);

        // Assert: workflow still running (other branches may continue)
        Assert.True(instance.Context.IsRunning);
        Assert.Equal(StepStatus.Failed, record.Status);
    }

    // ═══════════════════════════════════════════
    // 通用验证
    // ═══════════════════════════════════════════

    [Fact]
    public async Task HandleError_RecordsExceptionDetails()
    {
        // Arrange
        var instance = CreateInstance("wf-4");
        var record = CreateRecord("step-x");
        var ex = new InvalidOperationException("test error");

        // Act
        await _handler.HandleErrorAsync(instance, record, ex, ErrorPolicy.FailFast, CancellationToken.None);

        // Assert
        Assert.Equal(StepStatus.Failed, record.Status);
        Assert.Equal("test error", record.ErrorMessage);
        Assert.Contains("InvalidOperationException", record.ErrorDetail);
        Assert.NotNull(record.CompletedAt);
        Assert.True(record.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task HandleError_SavesCheckpoint()
    {
        // Arrange
        var instance = CreateInstance("wf-5");
        var record = CreateRecord("step-1");

        // Act
        await _handler.HandleErrorAsync(instance, record, new Exception("fail"), ErrorPolicy.ContinueOnError, CancellationToken.None);

        // Assert: checkpoint was saved
        var checkpoint = await _store.LoadAsync("wf-5");
        Assert.NotNull(checkpoint);
    }

    // ═══════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════

    private static WorkflowInstance CreateInstance(string instanceId)
    {
        var ctx = new WorkflowContext { InstanceId = instanceId };
        return new WorkflowInstance { Context = ctx, EntryStepId = "step-1" };
    }

    private static StepRecord CreateRecord(string stepId) => new()
    {
        StepId = stepId,
        Status = StepStatus.Running,
        StartedAt = DateTime.UtcNow,
    };

    private class InMemoryStateStore : IWorkflowStateStore
    {
        private readonly Dictionary<string, WorkflowCheckpoint> _store = new();

        public Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
        { _store[checkpoint.InstanceId] = checkpoint; return Task.CompletedTask; }

        public Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(instanceId, out var cp) ? cp : null);

        public Task DeleteAsync(string instanceId, CancellationToken ct = default)
        { _store.Remove(instanceId); return Task.CompletedTask; }

        public Task<List<string>> ListRunningAsync(CancellationToken ct = default)
            => Task.FromResult(_store.Values.Where(c => c.Status == "running").Select(c => c.InstanceId).ToList());

        public Task<List<string>> ListTimedOutAsync(CancellationToken ct = default)
            => Task.FromResult(_store.Values.Where(c => c.Status == "timed-out").Select(c => c.InstanceId).ToList());
    }
}

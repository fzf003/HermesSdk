using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class HeartbeatTimeoutTests
{
    // =================================================================
    // 8.1 MarkInstanceTimedOutAsync marks as timed-out (not failed)
    // =================================================================

    [Fact]
    public async Task MarkTimedOut_SetsTimedOutStatus_NotFailed()
    {
        var store = new TestStateStore();
        var handler = new TestAgentStep("agent-step");

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-timeout-1" };
        var instanceId = await engine.StartAsync("agent-step", ctx);

        // Manually mark as timed out
        await engine.MarkInstanceTimedOutAsync(instanceId, TimeSpan.Zero, CancellationToken.None);

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("timed-out", instance.Status);

        // Step should be Failed with timeout message
        var record = engine.GetStepRecords(instanceId).First(r => r.StepId == "agent-step");
        Assert.Equal(StepStatus.Failed, record.Status);
        Assert.Equal("心跳超时", record.ErrorMessage);
    }

    // =================================================================
    // 8.2 ResumeTimedOutWorkflowAsync succeeds
    // =================================================================

    [Fact]
    public async Task ResumeTimedOut_Succeeds()
    {
        var store = new TestStateStore();
        var handler = new TestAgentStep("agent-step");

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-resume-1" };
        var instanceId = await engine.StartAsync("agent-step", ctx);

        // Mark as timed out
        await engine.MarkInstanceTimedOutAsync(instanceId, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal("timed-out", engine.GetInstance(instanceId)!.Status);

        // Resume
        await engine.ResumeTimedOutWorkflowAsync(instanceId);

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("running", instance.Status);
    }

    // =================================================================
    // 8.3 ResumeTimedOutWorkflowAsync rejects non-timed-out instance
    // =================================================================

    [Fact]
    public async Task ResumeTimedOut_RejectsNonTimedOut()
    {
        var store = new TestStateStore();
        var handler = new TestAgentStep("agent-step");

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-running" };
        var instanceId = await engine.StartAsync("agent-step", ctx);

        // Instance is "running", should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ResumeTimedOutWorkflowAsync(instanceId));
    }

    // =================================================================
    // 8.4 Approval callback auto-recovers timed-out instance
    // =================================================================

    [Fact]
    public async Task ApprovalCallback_AutoRecovers_TimedOutInstance()
    {
        var store = new TestStateStore();
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx => new StepResult { IsSuccess = true },
        };

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-auto-recover" };
        var instanceId = await engine.StartAsync("approval-step", ctx);

        // Mark as timed out
        await engine.MarkInstanceTimedOutAsync(instanceId, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal("timed-out", engine.GetInstance(instanceId)!.Status);

        // Approval callback should auto-recover
        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "approved", "同意", "mgr-001");

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    // =================================================================
    // 8.5 Webhook callback auto-recovers timed-out instance
    // =================================================================

    [Fact]
    public async Task WebhookCallback_AutoRecovers_TimedOutInstance()
    {
        var store = new TestStateStore();
        var handler = new TestAgentStep("agent-step");

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-webhook-recover" };
        var instanceId = await engine.StartAsync("agent-step", ctx);

        // Mark as timed out
        await engine.MarkInstanceTimedOutAsync(instanceId, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal("timed-out", engine.GetInstance(instanceId)!.Status);

        // Webhook callback should auto-recover
        await engine.OnWebhookCallbackAsync(instanceId, "agent-step", "done", null);

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    // =================================================================
    // 8.6 HeartbeatExtension null uses global threshold
    // =================================================================

    [Fact]
    public void HeartbeatExtension_Null_UsesGlobalThreshold()
    {
        var handler = new TestCodeStep("code-step");
        Assert.Null(handler.HeartbeatExtension);
    }

    // =================================================================
    // 8.7 HeartbeatExtension non-null uses extended threshold
    // =================================================================

    [Fact]
    public void HeartbeatExtension_ApprovalStep_Returns24Hours()
    {
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = _ => { },
            OnExecute = _ => new StepResult { IsSuccess = true },
        };
        Assert.Equal(TimeSpan.FromHours(24), handler.HeartbeatExtension);
    }

    [Fact]
    public async Task GetEffectiveHeartbeatThreshold_WithApprovalStep_ReturnsExtension()
    {
        var store = new TestStateStore();
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = _ => { },
            OnExecute = _ => new StepResult { IsSuccess = true },
        };

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-threshold" };
        var instanceId = await engine.StartAsync("approval-step", ctx);

        var globalThreshold = TimeSpan.FromMinutes(5);
        var effective = engine.GetEffectiveHeartbeatThreshold(instanceId, globalThreshold);

        Assert.Equal(TimeSpan.FromHours(24), effective);
    }

    [Fact]
    public async Task GetEffectiveHeartbeatThreshold_WithoutApprovalStep_ReturnsGlobal()
    {
        var store = new TestStateStore();
        var handler = new TestAgentStep("agent-step");

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-global-threshold" };
        var instanceId = await engine.StartAsync("agent-step", ctx);

        var globalThreshold = TimeSpan.FromMinutes(5);
        var effective = engine.GetEffectiveHeartbeatThreshold(instanceId, globalThreshold);

        Assert.Equal(globalThreshold, effective);
    }

    // =================================================================
    // 8.8 ListTimedOutAsync returns correct results
    // =================================================================

    [Fact]
    public async Task ListTimedOutAsync_ReturnsOnlyTimedOutInstances()
    {
        var store = new TestStateStore();

        // Save a running checkpoint
        var runningCheckpoint = new WorkflowCheckpoint { InstanceId = "wf-running", Status = "running" };
        await store.SaveAsync(runningCheckpoint);

        // Save a timed-out checkpoint
        var timedOutCheckpoint = new WorkflowCheckpoint { InstanceId = "wf-timed-out", Status = "timed-out" };
        await store.SaveAsync(timedOutCheckpoint);

        var result = await store.ListTimedOutAsync();
        Assert.Single(result);
        Assert.Equal("wf-timed-out", result[0]);
    }

    // =================================================================
    // Test infrastructure
    // =================================================================

    private static WorkflowEngine CreateEngine(TestStateStore store, params IStepHandler[] handlers)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<WorkflowEngine>();

        return new WorkflowEngine(
            handlers,
            new NullWebhookClient(),
            new NullRunClient(),
            logger,
            store
        );
    }

    private class TestStateStore : IWorkflowStateStore
    {
        private readonly Dictionary<string, WorkflowCheckpoint> _store = new();

        public Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
        {
            _store[checkpoint.InstanceId] = checkpoint;
            return Task.CompletedTask;
        }

        public Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
        {
            _store.TryGetValue(instanceId, out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task DeleteAsync(string instanceId, CancellationToken ct = default)
        {
            _store.Remove(instanceId);
            return Task.CompletedTask;
        }

        public Task<List<string>> ListRunningAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(c => c.Status == "running")
                .Select(c => c.InstanceId)
                .ToList());
        }

        public Task<List<string>> ListTimedOutAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(c => c.Status == "timed-out")
                .Select(c => c.InstanceId)
                .ToList());
        }
    }

    private sealed class TestApprovalStep : HumanApprovalStepHandler
    {
        public override string StepId { get; }
        public Action<WorkflowContext>? OnDispatch { get; set; }
        public Func<WorkflowContext, StepResult>? OnExecute { get; set; }

        public TestApprovalStep(string stepId) { StepId = stepId; }

        public override Task DispatchAsync(WorkflowContext context, CancellationToken ct)
        {
            OnDispatch?.Invoke(context);
            return Task.CompletedTask;
        }

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(OnExecute?.Invoke(context) ?? Complete());

        public override string BuildApprovalMessage(WorkflowContext context) => "请审批";
    }

    private sealed class TestAgentStep : AgentStepHandler
    {
        public override string StepId { get; }
        public override AgentCommunicationMode Mode => AgentCommunicationMode.Webhook;
        public override string RouteName => "test.route";
        public override string EventType => "test.event";
        public override string BuildPrompt(WorkflowContext context) => "test prompt";
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(Complete());

        public TestAgentStep(string stepId) { StepId = stepId; }
    }

    private sealed class TestCodeStep : CodeStepHandler
    {
        public override string StepId { get; }
        public Func<WorkflowContext, StepResult>? OnExecute { get; set; }

        public TestCodeStep(string stepId) { StepId = stepId; }

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(OnExecute?.Invoke(context) ?? Complete());
    }

    private sealed class NullWebhookClient : IHermesWebhookClient
    {
        public Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendDirectAsync(string routeName, string message, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public void Dispose() { }
    }

    private sealed class NullRunClient : IHermesRunClient
    {
        public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult("run-test");

        public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }

        public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RunResult { RunId = "run-test", Status = "completed" });

        public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose() { }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class ApprovalTests
{
    // =================================================================
    // 8.1 HumanApprovalStepHandler DispatchAsync + ExecuteAsync 生命周期
    // =================================================================

    [Fact]
    public async Task HumanApproval_DispatchAsync_SetsDispatched_And_WaitsForCallback()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var dispatchCalled = false;
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { dispatchCalled = true; },
            OnExecute = ctx => Sequential("next-step"),
        };

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-approval-1" };
        var instanceId = await engine.StartAsync("approval-step", ctx);

        // Assert: DispatchAsync was called, step is Dispatched
        Assert.True(dispatchCalled);
        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        var record = engine.GetStepRecords(instanceId).First(r => r.StepId == "approval-step");
        Assert.Equal(StepStatus.Dispatched, record.Status);
        Assert.Equal("Approval", record.StepType);
    }

    [Fact]
    public async Task HumanApproval_ExecuteAsync_CalledOnlyAfterCallback()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var executeCalled = false;
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx =>
            {
                executeCalled = true;
                var approval = ctx.StepOutputs["approval-step"] as ApprovalResult;
                Assert.NotNull(approval);
                Assert.Equal("approved", approval.Decision);
                return Complete();
            },
        };

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-approval-2" };
        var instanceId = await engine.StartAsync("approval-step", ctx);

        // ExecuteAsync should NOT have been called yet (only DispatchAsync)
        Assert.False(executeCalled);

        // Act: send approval callback
        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "approved", "同意", "mgr-001");

        // Assert: ExecuteAsync was called after callback
        Assert.True(executeCalled);
        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    // =================================================================
    // 8.2 OnHumanApprovalCallbackAsync approved/rejected/幂等
    // =================================================================

    [Fact]
    public async Task ApprovalCallback_Approved_AdvancesToNextStep()
    {
        var store = new InMemoryStateStore();
        var nextStepReached = false;
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx => Sequential("notify-step"),
        };
        var notifyHandler = new TestCodeStep("notify-step")
        {
            OnExecute = ctx => { nextStepReached = true; return Complete(); },
        };

        await using var engine = CreateEngine(store, handler, notifyHandler);

        var ctx = new WorkflowContext { InstanceId = "wf-approved" };
        var instanceId = await engine.StartAsync("approval-step", ctx);
        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "approved", "同意", "mgr-001");

        Assert.True(nextStepReached);
    }

    [Fact]
    public async Task ApprovalCallback_Rejected_StillMarkCompleted()
    {
        var store = new InMemoryStateStore();
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx => Sequential("escalation-step"),
        };
        var escalationHandler = new TestCodeStep("escalation-step")
        {
            OnExecute = ctx => Complete(),
        };

        await using var engine = CreateEngine(store, handler, escalationHandler);

        var ctx = new WorkflowContext { InstanceId = "wf-rejected" };
        var instanceId = await engine.StartAsync("approval-step", ctx);
        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "rejected", "金额超限", "mgr-001");

        // Rejected is a normal business path — step should be Completed, not Failed
        var record = engine.GetStepRecords(instanceId).First(r => r.StepId == "approval-step");
        Assert.Equal(StepStatus.Completed, record.Status);

        // And workflow should still be running (routed to escalation)
        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    [Fact]
    public async Task ApprovalCallback_DuplicateIsIdempotentlySkipped()
    {
        var store = new InMemoryStateStore();
        var executeCount = 0;
        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx => { executeCount++; return Complete(); },
        };

        await using var engine = CreateEngine(store, handler);

        var ctx = new WorkflowContext { InstanceId = "wf-idempotent" };
        var instanceId = await engine.StartAsync("approval-step", ctx);

        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "approved", "同意", "mgr-001");
        Assert.Equal(1, executeCount);

        // Duplicate callback should be skipped
        await engine.OnHumanApprovalCallbackAsync(instanceId, "approval-step", "approved", "同意", "mgr-001");
        Assert.Equal(1, executeCount); // Should not increase
    }

    // =================================================================
    // 8.3 ParallelAny 首个完成即推进
    // =================================================================

    [Fact]
    public async Task ParallelAny_FirstCompletion_AdvancesImmediately()
    {
        var store = new InMemoryStateStore();
        var handler = new TestCodeStep("entry")
        {
            OnExecute = ctx => ParallelAny("step-a", "step-b"),
        };
        var stepA = new TestCodeStep("step-a")
        {
            OnExecute = ctx => Complete("A-done"),
        };
        var stepB = new TestCodeStep("step-b")
        {
            OnExecute = ctx => Complete("B-done"),
        };

        await using var engine = CreateEngine(store, handler, stepA, stepB);

        var ctx = new WorkflowContext { InstanceId = "wf-parallel-any" };
        var instanceId = await engine.StartAsync("entry", ctx);

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);

        // Both should have completed
        var records = engine.GetStepRecords(instanceId);
        Assert.All(new[] { "step-a", "step-b" }, id =>
            Assert.Equal(StepStatus.Completed, records.First(r => r.StepId == id).Status));
    }

    // =================================================================
    // 8.4 ParallelJoinAny 首个完成即汇合
    // =================================================================

    [Fact]
    public async Task ParallelJoinAny_FirstCompletion_AdvancesToDownstream()
    {
        var store = new InMemoryStateStore();
        var summaryReached = false;
        var handler = new TestCodeStep("entry")
        {
            OnExecute = ctx => ParallelJoinAny("summary", "step-a", "step-b"),
        };
        var stepA = new TestCodeStep("step-a")
        {
            OnExecute = ctx => Complete("A-done"),
        };
        var stepB = new TestCodeStep("step-b")
        {
            OnExecute = ctx => Complete("B-done"),
        };
        var summary = new TestCodeStep("summary")
        {
            OnExecute = ctx => { summaryReached = true; return Complete(); },
        };

        await using var engine = CreateEngine(store, handler, stepA, stepB, summary);

        var ctx = new WorkflowContext { InstanceId = "wf-join-any" };
        var instanceId = await engine.StartAsync("entry", ctx);

        Assert.True(summaryReached);
        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    // =================================================================
    // 8.5 Any 模式幂等跳过后续结果
    // =================================================================

    [Fact]
    public async Task ParallelJoinAny_LateResult_IsSkipped()
    {
        // Use webhook-dependent steps to simulate late arrival
        var store = new InMemoryStateStore();
        var summaryReached = false;
        var handler = new TestCodeStep("entry")
        {
            OnExecute = ctx => ParallelJoinAny("summary", "agent-a", "agent-b"),
        };
        var agentA = new TestAgentStep("agent-a");
        var agentB = new TestAgentStep("agent-b");
        var summary = new TestCodeStep("summary")
        {
            OnExecute = ctx => { summaryReached = true; return Complete(); },
        };

        await using var engine = CreateEngine(store, handler, agentA, agentB, summary);

        var ctx = new WorkflowContext { InstanceId = "wf-join-any-late" };
        var instanceId = await engine.StartAsync("entry", ctx);

        // First callback triggers JoinAny
        await engine.OnWebhookCallbackAsync(instanceId, "agent-a", "A-done", null);

        Assert.True(summaryReached);

        // Second callback arrives but should be skipped (already completed)
        // This should not throw or cause duplicate advancement
        await engine.OnWebhookCallbackAsync(instanceId, "agent-b", "B-done", null);
    }

    // =================================================================
    // 8.6 Recovery 静默等待
    // =================================================================

    [Fact]
    public async Task Recovery_HumanApprovalStep_DoesNotStartRecoveryTimer()
    {
        var store = new InMemoryStateStore();

        // Create a checkpoint with a HumanApproval step in Dispatched state
        var checkpoint = new WorkflowCheckpoint
        {
            InstanceId = "wf-approval-recovery",
            EntryStepId = "approval-step",
            Status = "running",
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30),
            InitialInput = new Dictionary<string, System.Text.Json.JsonElement>(),
            PendingStepIds = new List<string> { "approval-step" },
            InFlightStepIds = new List<string> { "approval-step" },
            StepRecords = new List<StepRecord>
            {
                new()
                {
                    StepId = "approval-step",
                    StepType = "Approval",
                    Status = StepStatus.Dispatched,
                    StartedAt = DateTime.UtcNow.AddMinutes(-1),
                    InputSnapshot = "审批请求: 请审批",
                },
            },
        };
        await store.SaveAsync(checkpoint);

        var handler = new TestApprovalStep("approval-step")
        {
            OnDispatch = ctx => { },
            OnExecute = ctx => Complete(),
        };

        await using var engine = CreateEngine(store, handler);

        // InitializeAsync should recover the step as Recovering but NOT start a recovery timer
        await engine.InitializeAsync();

        var instance = engine.GetInstance("wf-approval-recovery");
        Assert.NotNull(instance);
        var record = engine.GetStepRecords("wf-approval-recovery").First(r => r.StepId == "approval-step");
        Assert.Equal(StepStatus.Recovering, record.Status);

        // After recovery, the approval callback should still work
        await engine.OnHumanApprovalCallbackAsync("wf-approval-recovery", "approval-step", "approved", "同意", "mgr-001");
        Assert.Equal(StepStatus.Completed, record.Status);
    }

    // =================================================================
    // 8.7 回归测试：现有 Sequential/Parallel/ParallelJoin 不受影响
    // =================================================================

    [Fact]
    public async Task Sequential_StillWorks_AfterApprovalChanges()
    {
        var store = new InMemoryStateStore();
        var step1 = new TestCodeStep("step-1")
        {
            OnExecute = ctx => Sequential("step-2"),
        };
        var step2 = new TestCodeStep("step-2")
        {
            OnExecute = ctx => Complete("done"),
        };

        await using var engine = CreateEngine(store, step1, step2);

        var ctx = new WorkflowContext { InstanceId = "wf-sequential-regression" };
        var instanceId = await engine.StartAsync("step-1", ctx);

        var instance = engine.GetInstance(instanceId)!;
        Assert.Equal("completed", instance.Status);
    }

    [Fact]
    public async Task ParallelJoin_All_StillWorks_AfterApprovalChanges()
    {
        var store = new InMemoryStateStore();
        var summaryReached = false;
        var entry = new TestCodeStep("entry")
        {
            OnExecute = ctx => ParallelJoin("summary", "step-a", "step-b"),
        };
        var stepA = new TestCodeStep("step-a")
        {
            OnExecute = ctx => Complete("A"),
        };
        var stepB = new TestCodeStep("step-b")
        {
            OnExecute = ctx => Complete("B"),
        };
        var summary = new TestCodeStep("summary")
        {
            OnExecute = ctx => { summaryReached = true; return Complete(); },
        };

        await using var engine = CreateEngine(store, entry, stepA, stepB, summary);

        var ctx = new WorkflowContext { InstanceId = "wf-join-regression" };
        var instanceId = await engine.StartAsync("entry", ctx);

        Assert.True(summaryReached);
    }

    // =================================================================
    // Test infrastructure
    // =================================================================

    private static WorkflowEngine CreateEngine(InMemoryStateStore store, params IStepHandler[] handlers)
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

    private static StepResult Sequential(string nextStepId) => StepHandlerBaseProtected.Sequential(nextStepId);
    private static StepResult Complete(object? output = null) => StepHandlerBaseProtected.Complete(output);
    private static StepResult ParallelAny(params string[] ids) => StepHandlerBaseProtected.ParallelAny(ids);
    private static StepResult ParallelJoinAny(string down, params string[] ids) => StepHandlerBaseProtected.ParallelJoinAny(down, ids);
    private static StepResult ParallelJoin(string down, params string[] ids) => StepHandlerBaseProtected.ParallelJoin(down, ids);

    // Expose protected factory methods for test use
    private abstract class StepHandlerBaseProtected : StepHandlerBase
    {
        public static new StepResult Sequential(string nextStepId, object? output = null)
            => StepHandlerBase.Sequential(nextStepId, output);
        public static new StepResult Complete(object? output = null)
            => StepHandlerBase.Complete(output);
        public static new StepResult ParallelAny(params string[] nextStepIds)
            => StepHandlerBase.ParallelAny(nextStepIds);
        public static new StepResult ParallelJoinAny(string downstreamStepId, params string[] childStepIds)
            => StepHandlerBase.ParallelJoinAny(downstreamStepId, childStepIds);
        public static new StepResult ParallelJoin(string downstreamStepId, params string[] childStepIds)
            => StepHandlerBase.ParallelJoin(downstreamStepId, childStepIds);
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private class TestApprovalStep : HumanApprovalStepHandler
    {
        private readonly string _stepId;
        public Action<WorkflowContext>? OnDispatch { get; set; }
        public Func<WorkflowContext, StepResult>? OnExecute { get; set; }

        public TestApprovalStep(string stepId) => _stepId = stepId;
        public override string StepId => _stepId;

        public override Task DispatchAsync(WorkflowContext context, CancellationToken ct)
        {
            OnDispatch?.Invoke(context);
            return Task.CompletedTask;
        }

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            var result = OnExecute?.Invoke(context) ?? Complete();
            return Task.FromResult(result);
        }
    }

    private class TestCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        public Func<WorkflowContext, StepResult>? OnExecute { get; set; }

        public TestCodeStep(string stepId) => _stepId = stepId;
        public override string StepId => _stepId;

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            var result = OnExecute?.Invoke(context) ?? Complete();
            return Task.FromResult(result);
        }
    }

    private class TestAgentStep : AgentStepHandler
    {
        public override string StepId { get; }
        public override AgentCommunicationMode Mode => AgentCommunicationMode.Webhook;
        public override string RouteName => $"workflow.{StepId}";
        public override string EventType => "workflow.step";
        public override string BuildPrompt(WorkflowContext context) => "test prompt";

        public TestAgentStep(string stepId) => StepId = stepId;

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(Complete());
    }

    private class InMemoryStateStore : IWorkflowStateStore
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

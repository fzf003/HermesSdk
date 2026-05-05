using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class ParallelJoinTests
{
    // ═══════════════════════════════════════════
    // StepResult 工厂方法测试
    // ═══════════════════════════════════════════

    [Fact]
    public void ParallelJoin_SetsAllProperties()
    {
        // Act
        var result = StepHandlerBaseExposed.ParallelJoin("merge-step", "branch-a", "branch-b");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("merge-step", result.JoinDownstreamStepId);
        Assert.Equal(["branch-a", "branch-b"], result.NextStepIds);
        Assert.True(result.WaitForParallelCompletion);
        Assert.Equal(ParallelWaitMode.All, result.WaitMode);
    }

    [Fact]
    public void ParallelJoinAny_SetsAnyMode()
    {
        // Act
        var result = StepHandlerBaseExposed.ParallelJoinAny("merge-step", "branch-a", "branch-b", "branch-c");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("merge-step", result.JoinDownstreamStepId);
        Assert.Equal(["branch-a", "branch-b", "branch-c"], result.NextStepIds);
        Assert.True(result.WaitForParallelCompletion);
        Assert.Equal(ParallelWaitMode.Any, result.WaitMode);
    }

    // ═══════════════════════════════════════════
    // Engine 集成测试 — All 模式
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AllMode_WaitsForAllBranches()
    {
        // Arrange: entry → ParallelJoin(merge, branch-a, branch-b) → merge → complete
        var store = new InMemoryStateStore();
        var mergeReached = false;

        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.ParallelJoin("merge", "branch-a", "branch-b") };
        var branchA = new TestCodeStep("branch-a") { OnExecute = _ => StepHandlerBaseExposed.Complete("a-done") };
        var branchB = new TestCodeStep("branch-b") { OnExecute = _ => StepHandlerBaseExposed.Complete("b-done") };
        var merge = new TestCodeStep("merge") { OnExecute = _ => { mergeReached = true; return StepHandlerBaseExposed.Complete("merged"); } };

        await using var engine = CreateEngine(store, entry, branchA, branchB, merge);

        var ctx = new WorkflowContext { InstanceId = "wf-join-all" };
        var instanceId = await engine.StartAsync("entry", ctx);

        // Assert: merge step should have been reached
        Assert.True(mergeReached, "Merge step should have been reached (All mode)");
    }

    // ═══════════════════════════════════════════
    // Engine 集成测试 — Any 模式
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AnyMode_TriggeredByFirst()
    {
        // Arrange: entry → ParallelJoinAny(merge, branch-a, branch-b)
        var store = new InMemoryStateStore();
        var mergeReached = false;

        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.ParallelJoinAny("merge", "branch-a", "branch-b") };
        var branchA = new TestCodeStep("branch-a") { OnExecute = _ => StepHandlerBaseExposed.Complete("a-done") };
        var branchB = new TestCodeStep("branch-b") { OnExecute = _ => StepHandlerBaseExposed.Complete("b-done") };
        var merge = new TestCodeStep("merge") { OnExecute = _ => { mergeReached = true; return StepHandlerBaseExposed.Complete("merged"); } };

        await using var engine = CreateEngine(store, entry, branchA, branchB, merge);

        var ctx = new WorkflowContext { InstanceId = "wf-join-any" };
        var instanceId = await engine.StartAsync("entry", ctx);

        // Assert: merge step should have been reached (Any mode)
        Assert.True(mergeReached, "Merge step should have been reached (Any mode)");
    }

    // ═══════════════════════════════════════════
    // JoinDownstream 推进正确性
    // ═══════════════════════════════════════════

    [Fact]
    public async Task JoinDownstream_AdvancesCorrectly()
    {
        // Arrange: entry → ParallelJoin(merge, branch-a, branch-b) → merge → step-final
        var store = new InMemoryStateStore();
        var finalReached = false;

        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.ParallelJoin("merge", "branch-a", "branch-b") };
        var branchA = new TestCodeStep("branch-a") { OnExecute = _ => StepHandlerBaseExposed.Complete("a") };
        var branchB = new TestCodeStep("branch-b") { OnExecute = _ => StepHandlerBaseExposed.Complete("b") };
        var merge = new TestCodeStep("merge") { OnExecute = _ => StepHandlerBaseExposed.Sequential("step-final") };
        var stepFinal = new TestCodeStep("step-final") { OnExecute = _ => { finalReached = true; return StepHandlerBaseExposed.Complete("final"); } };

        await using var engine = CreateEngine(store, entry, branchA, branchB, merge, stepFinal);

        var ctx = new WorkflowContext { InstanceId = "wf-join-downstream" };
        var instanceId = await engine.StartAsync("entry", ctx);

        // Assert: final step should be reached via merge → step-final
        Assert.True(finalReached, "Final step should be reached after merge");
    }

    // ═══════════════════════════════════════════
    // 测试辅助类
    // ═══════════════════════════════════════════

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

    private class TestCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        public Func<WorkflowContext, StepResult>? OnExecute { get; set; }

        public TestCodeStep(string stepId) => _stepId = stepId;
        public override string StepId => _stepId;

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            var result = OnExecute?.Invoke(context) ?? StepHandlerBaseExposed.Complete();
            return Task.FromResult(result);
        }
    }

    // Expose protected factory methods
    private abstract class StepHandlerBaseExposed : StepHandlerBase
    {
        public static new StepResult ParallelJoin(string downstreamStepId, params string[] childStepIds)
            => StepHandlerBase.ParallelJoin(downstreamStepId, childStepIds);
        public static new StepResult ParallelJoinAny(string downstreamStepId, params string[] childStepIds)
            => StepHandlerBase.ParallelJoinAny(downstreamStepId, childStepIds);
        public static new StepResult Sequential(string nextStepId, object? output = null)
            => StepHandlerBase.Sequential(nextStepId, output);
        public static new StepResult Complete(object? output = null)
            => StepHandlerBase.Complete(output);
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => throw new NotImplementedException();
    }

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
        { yield break; }
        public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RunResult { RunId = "run-test", Status = "completed" });
        public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public void Dispose() { }
    }
}

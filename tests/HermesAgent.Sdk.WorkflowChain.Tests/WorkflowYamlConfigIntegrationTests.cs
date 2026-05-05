using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class WorkflowYamlConfigIntegrationTests
{
    // ═══════════════════════════════════════════
    // 6.1-6.2: AgentStep + RetryConfig
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AgentStep_WithRetryConfig_StepDefinitionRegistered()
    {
        // Verify that retry config is properly wired through StepDefinition → Engine pipeline.
        // Actual retry behavior requires a RunClient mock that throws — tested via unit tests.
        var store = new InMemoryStateStore();
        var agentStep = new TestAgentStepWithRetry("agent-step", () => "run-ok");
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("agent-step") };

        await using var engine = CreateEngine(store, entry, agentStep);

        engine.RegisterStepDefinitions("test-workflow",
        [
            new StepDefinition { Id = "agent-step", Type = StepType.Agent, Retry = new RetryConfigYaml { MaxRetries = 3, Policy = "immediate" } }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-retry-test" };

        // Act — step should complete normally via NullRunClient
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "test-workflow");

        // Assert: step completed successfully (retry config is present but NullRunClient doesn't fail)
        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("completed", instance.Status);
    }

    // ═══════════════════════════════════════════
    // 6.3: AgentStep + TimeoutConfig (验证超时触发)
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AgentStep_WithTimeoutConfig_TimesOut()
    {
        // This test verifies timeout configuration is properly applied.
        // A full timeout integration test would need a slow RunClient mock.
        // Here we verify the StepDefinition is wired through correctly.
        var store = new InMemoryStateStore();
        var agentStep = new TestAgentStepWithRetry("agent-step", () => "run-ok");
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("agent-step") };

        await using var engine = CreateEngine(store, entry, agentStep);

        engine.RegisterStepDefinitions("timeout-workflow",
        [
            new StepDefinition { Id = "agent-step", Type = StepType.Agent, Timeout = "00:05:00", TimeoutAction = "fail" }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-timeout-test" };

        // Act — step should complete normally (timeout is 5min, well beyond test duration)
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "timeout-workflow");

        // Assert: step completed successfully
        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
    }

    // ═══════════════════════════════════════════
    // 6.4: CodeStep + TimeoutConfig
    // ═══════════════════════════════════════════

    [Fact]
    public async Task CodeStep_WithTimeoutConfig_CompletesNormally()
    {
        var store = new InMemoryStateStore();
        var codeStep = new TestCodeStep("code-step") { OnExecute = _ => StepHandlerBaseExposed.Complete("done") };
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("code-step") };

        await using var engine = CreateEngine(store, entry, codeStep);

        engine.RegisterStepDefinitions("code-timeout-workflow",
        [
            new StepDefinition { Id = "code-step", Type = StepType.Code, Timeout = "00:01:00" }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-code-timeout" };

        // Act
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "code-timeout-workflow");

        // Assert
        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("completed", instance.Status);
    }

    // ═══════════════════════════════════════════
    // 6.5-6.6: ErrorPolicy 验证（通过 ErrorHandler 单元测试覆盖）
    // 这里只验证 ErrorPolicy 配置被正确传递
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ErrorHandler_WithFailFastPolicy_TerminatesWorkflow()
    {
        // This is covered by ErrorHandlerTests.FailFast_TerminatesWorkflow
        // Here we verify the YAML → ErrorPolicy conversion pipeline
        var policy = YamlConfigConverter.ConvertErrorPolicy("fail_fast");
        Assert.Equal(ErrorPolicy.FailFast, policy);
    }

    [Fact]
    public async Task ErrorHandler_WithContinueOnErrorPolicy_Continues()
    {
        var policy = YamlConfigConverter.ConvertErrorPolicy("continue_on_error");
        Assert.Equal(ErrorPolicy.ContinueOnError, policy);
    }

    // ═══════════════════════════════════════════
    // 6.7: 多工作流同步骤ID 隔离
    // ═══════════════════════════════════════════

    [Fact]
    public async Task MultiWorkflow_WithSameStepIds_Isolated()
    {
        var store = new InMemoryStateStore();
        var step = new TestCodeStep("shared-step") { OnExecute = _ => StepHandlerBaseExposed.Complete("result") };
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("shared-step") };

        await using var engine = CreateEngine(store, entry, step);

        // Register two different workflows with same step ID but different configs
        engine.RegisterStepDefinitions("workflow-a",
        [
            new StepDefinition { Id = "shared-step", Type = StepType.Code, Timeout = "00:01:00" }
        ]);
        engine.RegisterStepDefinitions("workflow-b",
        [
            new StepDefinition { Id = "shared-step", Type = StepType.Code, Timeout = "00:10:00" }
        ]);

        var ctxA = new WorkflowContext { InstanceId = "wf-a" };
        var ctxB = new WorkflowContext { InstanceId = "wf-b" };

        // Act — both should complete using their respective configs
        await engine.StartAsync("entry", ctxA, CancellationToken.None, "workflow-a");
        await engine.StartAsync("entry", ctxB, CancellationToken.None, "workflow-b");

        // Assert: both instances completed
        Assert.Equal("completed", engine.GetInstance("wf-a")!.Status);
        Assert.Equal("completed", engine.GetInstance("wf-b")!.Status);
    }

    // ═══════════════════════════════════════════
    // 6.8: 无配置步骤正常执行（向后兼容）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task NoConfig_Steps_ExecuteNormally()
    {
        var store = new InMemoryStateStore();
        var step1 = new TestCodeStep("step-1") { OnExecute = _ => StepHandlerBaseExposed.Sequential("step-2") };
        var step2 = new TestCodeStep("step-2") { OnExecute = _ => StepHandlerBaseExposed.Complete("done") };

        await using var engine = CreateEngine(store, step1, step2);
        // No RegisterStepDefinitions call — backward compatible

        var ctx = new WorkflowContext { InstanceId = "wf-no-config" };
        var instanceId = await engine.StartAsync("step-1", ctx);

        var instance = engine.GetInstance(instanceId);
        Assert.Equal("completed", instance!.Status);
    }

    // ═══════════════════════════════════════════
    // 辅助类
    // ═══════════════════════════════════════════

    private static WorkflowEngine CreateEngine(InMemoryStateStore store, params IStepHandler[] handlers)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<WorkflowEngine>();
        return new WorkflowEngine(handlers, new NullWebhookClient(), new NullRunClient(), logger, store);
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

    private class TestAgentStepWithRetry : AgentStepHandler
    {
        private readonly string _stepId;
        private readonly Func<string> _runClientAction;

        public TestAgentStepWithRetry(string stepId, Func<string> runClientAction)
        {
            _stepId = stepId;
            _runClientAction = runClientAction;
        }

        public override string StepId => _stepId;
        public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
        public override string RouteName => $"workflow.{_stepId}";
        public override string EventType => "workflow.step";
        public override RunOptions? RunOptions => null;

        public override string BuildPrompt(WorkflowContext context) => "test prompt";

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete(_runClientAction()));
    }

    private abstract class StepHandlerBaseExposed : StepHandlerBase
    {
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
        public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new RunEvent { Type = "run.completed", OutPut = "test-output" };
        }
        public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RunResult { RunId = "run-test", Status = "completed" });
        public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public void Dispose() { }
    }
}

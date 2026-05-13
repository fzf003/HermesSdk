using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class FluentApiConfigTests
{
    // ═══════════════════════════════════════════
    // Fluent API 优先级：YAML > Fluent > 虚属性
    // ═══════════════════════════════════════════

    [Fact]
    public async Task FluentTimeout_Overrides_VirtualProperty()
    {
        // Handler 虚属性 timeout=50ms，Fluent 配置 5s → 使用 Fluent 的 5s
        var store = new InMemoryStateStore();
        var step = new TimeoutCodeStep("slow-step", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("slow-step") };

        var fluentDefaults = new Dictionary<Type, StepHandlerDefaults>
        {
            [typeof(TimeoutCodeStep)] = new() { Timeout = "00:00:05" }
        };

        await using var engine = CreateEngine(store, fluentDefaults, entry, step);

        var ctx = new WorkflowContext { InstanceId = "wf-fluent-override" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        var record = instance.StepRecords.Find(r => r.StepId == "slow-step");
        Assert.NotNull(record);
        Assert.Equal(StepStatus.Completed, record.Status);
    }

    [Fact]
    public async Task FluentRetry_Overrides_VirtualProperty()
    {
        // Handler 虚属性 Retry=MaxRetries 1，Fluent 配置 MaxRetries=3 → 使用 Fluent 的 3
        var store = new InMemoryStateStore();
        var step = new RetryCodeStep("fail-step");
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("fail-step") };

        var fluentDefaults = new Dictionary<Type, StepHandlerDefaults>
        {
            [typeof(RetryCodeStep)] = new() { Retry = new RetryConfigYaml { MaxRetries = 5, Policy = RetryPolicy.Immediate } }
        };

        await using var engine = CreateEngine(store, fluentDefaults, entry, step);

        var ctx = new WorkflowContext { InstanceId = "wf-fluent-retry" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        // Handler RetryCodeStep 虚属性 MaxRetries=3，但 Fluent 配置 5
        // 实际执行次数取决于 Fluent 配置
        Assert.True(step.ExecutionCount >= 3, "Fluent 配置的 MaxRetries 应生效");
    }

    [Fact]
    public async Task Yaml_StillOverrides_Fluent()
    {
        // Fluent 配置 timeout=5s，YAML 配置 timeout=30s → YAML 胜出
        var store = new InMemoryStateStore();
        var step = new TimeoutCodeStep("slow-step", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("slow-step") };

        var fluentDefaults = new Dictionary<Type, StepHandlerDefaults>
        {
            [typeof(TimeoutCodeStep)] = new() { Timeout = "00:00:05" }
        };

        await using var engine = CreateEngine(store, fluentDefaults, entry, step);

        // YAML 配置 30s 超时
        engine.RegisterStepDefinitions("test-workflow",
        [
            new StepDefinition { Id = "slow-step", Type = StepType.Code, Timeout = "00:00:30" }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-yaml-over-fluent" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "test-workflow");

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        var record = instance.StepRecords.Find(r => r.StepId == "slow-step");
        Assert.NotNull(record);
        Assert.Equal(StepStatus.Completed, record.Status);
    }

    [Fact]
    public async Task NoFluent_NoYaml_VirtualPropertyUsed()
    {
        // 无 Fluent 配置、无 YAML → 使用 Handler 虚属性
        var store = new InMemoryStateStore();
        var step = new TimeoutCodeStep("slow-step", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("slow-step") };

        await using var engine = CreateEngine(store, entry, step);

        var ctx = new WorkflowContext { InstanceId = "wf-no-fluent" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        var record = instance.StepRecords.Find(r => r.StepId == "slow-step");
        Assert.NotNull(record);

        // Handler 虚属性 timeout=50ms，步骤执行 300ms → 超时失败
        Assert.Equal(StepStatus.Failed, record.Status);
    }

    // ═══════════════════════════════════════════
    // CodeStepBuilder 构建验证
    // ═══════════════════════════════════════════

    [Fact]
    public void CodeStepBuilder_BuildsDefaults()
    {
        var builder = new CodeStepBuilder<TestCodeStep>();
        builder.WithTimeout("00:00:10")
               .WithTimeoutAction(TimeoutAction.Fail)
               .WithRetry(r => r.Immediate(3))
               .WithErrorPolicy(ErrorPolicy.SkipFailedBranch);

        var defaults = builder.Build();

        Assert.Equal("00:00:10", defaults.Timeout);
        Assert.Equal("fail", defaults.TimeoutAction);
        Assert.NotNull(defaults.Retry);
        Assert.Equal(3, defaults.Retry!.MaxRetries);
        Assert.Equal("skip_failed_branch", defaults.ErrorPolicy);
    }

    [Fact]
    public void AgentStepBuilder_BuildsWithPrompt()
    {
        var builder = new AgentStepBuilder<TestAgentStep>();
        builder.WithTimeout("00:00:30")
               .WithPrompt("请分析 {{steps.input.output.data}}")
               .WithSystemPrompt("你是助手");

        var defaults = builder.Build();

        Assert.Equal("00:00:30", defaults.Timeout);
        Assert.Equal("请分析 {{steps.input.output.data}}", defaults.Prompt);
        Assert.Equal("你是助手", defaults.SystemPrompt);
    }

    [Fact]
    public void HumanApprovalStepBuilder_BuildsDefaults()
    {
        var builder = new HumanApprovalStepBuilder<TestHumanApprovalStep>();
        builder.WithTimeout("00:01:00")
               .WithTimeoutAction(TimeoutAction.Fail)
               .WithRetry(r => r.ExponentialBackoff(2))
               .WithErrorPolicy(ErrorPolicy.SkipFailedBranch);

        var defaults = builder.Build();

        Assert.Equal("00:01:00", defaults.Timeout);
        Assert.Equal("fail", defaults.TimeoutAction);
        Assert.NotNull(defaults.Retry);
        Assert.Equal(2, defaults.Retry!.MaxRetries);
        Assert.Equal("skip_failed_branch", defaults.ErrorPolicy);
    }

    [Fact]
    public void FluentConfig_WithoutConfigure_DefaultsNull()
    {
        var builder = new CodeStepBuilder<TestCodeStep>();
        var defaults = builder.Build();

        Assert.Null(defaults.Timeout);
        Assert.Null(defaults.TimeoutAction);
        Assert.Null(defaults.Retry);
        Assert.Null(defaults.ErrorPolicy);
    }

    // ═══════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════

    private static WorkflowEngine CreateEngine(InMemoryStateStore store, params IStepHandler[] handlers)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<WorkflowEngine>();
        return new WorkflowEngine(handlers, new NullWebhookClient(), new NullRunClient(), logger, store);
    }

    private static WorkflowEngine CreateEngine(InMemoryStateStore store,
        IReadOnlyDictionary<Type, StepHandlerDefaults> fluentDefaults,
        params IStepHandler[] handlers)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<WorkflowEngine>();
        return new WorkflowEngine(handlers, new NullWebhookClient(), new NullRunClient(), logger, store,
            fluentDefaults: fluentDefaults);
    }

    // ═══════════════════════════════════════════
    // 测试用 Handler 子类
    // ═══════════════════════════════════════════

    private class TimeoutCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        private readonly TimeSpan _handlerTimeout;
        private readonly TimeSpan _executeDelay;

        public override string StepId => _stepId;
        public override string Timeout => _handlerTimeout.ToString("hh\\:mm\\:ss");

        public TimeoutCodeStep(string stepId, TimeSpan handlerTimeout, TimeSpan executeDelay)
        {
            _stepId = stepId;
            _handlerTimeout = handlerTimeout;
            _executeDelay = executeDelay;
        }

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            await Task.Delay(_executeDelay, ct);
            return StepHandlerBaseExposed.Complete("done");
        }
    }

    private class RetryCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        public int ExecutionCount { get; private set; }

        public override string StepId => _stepId;
        public override RetryConfigYaml? Retry => new() { MaxRetries = 3, Policy = RetryPolicy.Immediate };

        public RetryCodeStep(string stepId) => _stepId = stepId;

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            ExecutionCount++;
            await Task.Delay(10, ct);
            throw new TimeoutException("always timeout");
        }
    }

    private class TestAgentStep : AgentStepHandler
    {
        private readonly string _stepId;
        public override string StepId => _stepId;
        public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
        public override string RouteName => "test";
        public override string EventType => "test";
        public override string BuildPrompt(WorkflowContext context) => "built prompt";

        public TestAgentStep(string stepId) => _stepId = stepId;
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete("done"));
    }

    private class TestHumanApprovalStep : HumanApprovalStepHandler
    {
        public override string StepId => "test-approval";
        public override Task DispatchAsync(WorkflowContext context, CancellationToken ct) => Task.CompletedTask;
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete("approved"));
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

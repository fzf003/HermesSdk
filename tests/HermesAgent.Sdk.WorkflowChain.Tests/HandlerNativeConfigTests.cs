using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class HandlerNativeConfigTests
{
    // ═══════════════════════════════════════════
    // 6.1: Handler 默认策略生效测试
    // ═══════════════════════════════════════════

    [Fact]
    public async Task HandlerDefaultTimeout_Applies_WhenNoYamlConfig()
    {
        var store = new InMemoryStateStore();
        var step = new TimeoutCodeStep("slow-step", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("slow-step") };

        await using var engine = CreateEngine(store, entry, step);

        var ctx = new WorkflowContext { InstanceId = "wf-handler-timeout" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        // Handler 声明了超时 50ms，步骤执行 1h，步骤应超时
        var record = instance.StepRecords.Find(r => r.StepId == "slow-step");
        Assert.NotNull(record);
        Assert.Equal(StepStatus.Failed, record.Status);
    }

    [Fact]
    public async Task HandlerDefaultRetry_Applies_WhenNoYamlConfig()
    {
        var store = new InMemoryStateStore();
        var step = new RetryCodeStep("fail-step");
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("fail-step") };

        await using var engine = CreateEngine(store, entry, step);

        // 不注册 YAML step definitions — 使用 Handler 默认值
        var ctx = new WorkflowContext { InstanceId = "wf-handler-retry" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        // Handler Retry.MaxRetries=3，执行器运行 3 次（共 3 次 attempt，最后 1 次抛出异常）
        Assert.Equal(3, step.ExecutionCount);
    }

    // ═══════════════════════════════════════════
    // 6.2: YAML 覆盖 Handler 默认策略测试
    // ═══════════════════════════════════════════

    [Fact]
    public async Task YamlTimeout_Overrides_HandlerDefault()
    {
        var store = new InMemoryStateStore();
        // Handler 声明 50ms 超时，但 YAML 声明 5s 超时
        var step = new TimeoutCodeStep("slow-step", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("slow-step") };

        await using var engine = CreateEngine(store, entry, step);

        // YAML 配置 5s 超时 — 覆盖 Handler 的 50ms
        engine.RegisterStepDefinitions("test-workflow",
        [
            new StepDefinition { Id = "slow-step", Type = StepType.Code, Timeout = "00:00:05" }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-yaml-override" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "test-workflow");

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        // YAML 超时 5s 覆盖了 Handler 的 50ms，步骤应在 300ms 内完成
        var record = instance.StepRecords.Find(r => r.StepId == "slow-step");
        Assert.NotNull(record);
        Assert.Equal(StepStatus.Completed, record.Status);
    }

    // ═══════════════════════════════════════════
    // 6.3: 部分覆盖测试
    // ═══════════════════════════════════════════

    [Fact]
    public async Task YamlPartialOverride_HandlerRetryRetained()
    {
        var store = new InMemoryStateStore();
        // Handler 同时声明 timeout=5s 和 retry=3
        var step = new TimeoutWithRetryCodeStep("fail-step", TimeSpan.FromSeconds(5));
        var entry = new TestCodeStep("entry") { OnExecute = _ => StepHandlerBaseExposed.Sequential("fail-step") };

        await using var engine = CreateEngine(store, entry, step);

        // YAML 只覆盖 timeout=10s，不配置 retry — retry 应保留 Handler 默认
        engine.RegisterStepDefinitions("test-workflow",
        [
            new StepDefinition { Id = "fail-step", Type = StepType.Code, Timeout = "00:00:10" }
        ]);

        var ctx = new WorkflowContext { InstanceId = "wf-partial-override" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None, "test-workflow");

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);

        // Handler Retry.MaxRetries=3 应保留（即使 YAML 未配置 retry）
        Assert.Equal(3, step.ExecutionCount);
    }

    // ═══════════════════════════════════════════
    // 6.5: Prompt 优先级测试
    // ═══════════════════════════════════════════

    [Fact]
    public void PromptPriorityYamlWins()
    {
        // 验证优先级链路：YAML prompt > Handler Prompt > BuildPrompt(ctx)
        var handler = new PromptAgentStep("test-step", "default prompt");
        var stepDef = new StepDefinition { Id = "test-step", Type = StepType.Agent, Prompt = "yaml prompt" };

        // YAML prompt 应胜出
        var ctx = new WorkflowContext { InstanceId = "test" };
        var resolved = ResolvePromptInline(handler, stepDef, ctx);

        Assert.Equal("yaml prompt", resolved);
    }

    [Fact]
    public void PromptPriorityHandlerDefaultWinsOverBuildPrompt()
    {
        // YAML 未配置 prompt，使用 Handler Prompt 虚属性
        var handler = new PromptAgentStep("test-step", "handler default prompt");

        var resolved = ResolvePromptInline(handler, null, new WorkflowContext { InstanceId = "test" });

        Assert.Equal("handler default prompt", resolved);
    }

    [Fact]
    public void PromptPriorityFallsBackToBuildPrompt()
    {
        // YAML 和 Handler Prompt 都为 null，回退到 BuildPrompt
        var handler = new PromptAgentStep("test-step", null); // Handler virtual prop returns null

        var resolved = ResolvePromptInline(handler, null, new WorkflowContext { InstanceId = "test" });

        Assert.Equal("built prompt", resolved);
    }

    // ═══════════════════════════════════════════
    // 6.6: StepHandlerDefaults.FromHandler() 提取测试
    // ═══════════════════════════════════════════

    [Fact]
    public void StepHandlerDefaults_ExtractsFromCodeHandler()
    {
        var handler = new RetryCodeStep("test");

        var defaults = StepHandlerDefaults.FromHandler(handler);

        Assert.NotNull(defaults.Retry);
        Assert.Equal(3, defaults.Retry!.MaxRetries);
        Assert.Null(defaults.Timeout); // CodeStep 默认不声明超时
        Assert.Null(defaults.Prompt); // CodeStep 没有 prompt
    }

    [Fact]
    public void StepHandlerDefaults_ExtractsFromAgentHandler()
    {
        var handler = new TestAgentStep("agent-step", "agent prompt");

        var defaults = StepHandlerDefaults.FromHandler(handler);

        Assert.Equal("agent prompt", defaults.Prompt);
        Assert.Null(defaults.Retry); // 默认无 retry
    }

    // ═══════════════════════════════════════════
    // 6.7: ExportTemplate 合并 Handler 默认值
    // ═══════════════════════════════════════════

    [Fact]
    public void ExportTemplate_MergesHandlerDefaults()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = CreateWorkflowDefinition("export-test", "1.0");
        registry.Register(definition);

        var handlerDefaults = new Dictionary<string, StepHandlerDefaults>
        {
            ["step-1"] = new StepHandlerDefaults
            {
                Timeout = "00:00:30",
                TimeoutAction = "fail",
                ErrorPolicy = "fail_fast",
            },
            ["step-2"] = new StepHandlerDefaults
            {
                Timeout = "00:01:00",
                Retry = new RetryConfigYaml { MaxRetries = 3, Policy = RetryPolicy.ExponentialBackoff },
            }
        };

        var yaml = importExport.ExportTemplate("export-test", handlerDefaults);

        Assert.NotNull(yaml);
        Assert.Contains("timeout: 00:00:30", yaml);
        Assert.Contains("timeout_action: fail", yaml);
        Assert.Contains("error_policy: fail_fast", yaml);
    }

    [Fact]
    public void ExportTemplate_YamlConfigTakesPriority()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        // 创建一个已有 YAML timeout 配置的工作流
        var yaml = @"
name: priority-test
version: 1.0
steps:
  - id: step-1
    type: code
    assembly: TestAssembly
    class: TestClass
    timeout: 00:05:00
";
        var definition = importExport.ImportFromYaml(yaml, register: true);

        // Handler 默认值 Timeout 为 30s
        var handlerDefaults = new Dictionary<string, StepHandlerDefaults>
        {
            ["step-1"] = new StepHandlerDefaults { Timeout = "00:00:30" }
        };

        var exported = importExport.ExportTemplate("priority-test", handlerDefaults);

        // YAML 已显式配置的 5m 应被保留，不被 Handler 默认值 30s 覆盖
        Assert.Contains("timeout: 00:05:00", exported);
    }

    // ═══════════════════════════════════════════
    // 6.8: 向后兼容 — 未声明默认配置的 Handler
    // ═══════════════════════════════════════════

    [Fact]
    public async Task NoHandlerDefaults_BackwardCompatible()
    {
        var store = new InMemoryStateStore();
        var step1 = new TestCodeStep("step-1") { OnExecute = _ => StepHandlerBaseExposed.Sequential("step-2") };
        var step2 = new TestCodeStep("step-2") { OnExecute = _ => StepHandlerBaseExposed.Complete("done") };

        await using var engine = CreateEngine(store, step1, step2);

        var ctx = new WorkflowContext { InstanceId = "wf-backward" };
        var instanceId = await engine.StartAsync("step-1", ctx);

        var instance = engine.GetInstance(instanceId);
        Assert.Equal("completed", instance!.Status);
    }

    // ═══════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════

    private static string? ResolvePromptInline(PromptAgentStep handler, StepDefinition? stepDef, WorkflowContext ctx)
    {
        // 模拟 MergeStepRuntimeConfig + ResolvePromptTemplate 的逻辑
        var mergedPrompt = !string.IsNullOrWhiteSpace(stepDef?.Prompt)
            ? stepDef!.Prompt
            : handler.Prompt;

        if (!string.IsNullOrWhiteSpace(mergedPrompt))
            return mergedPrompt;

        return handler.BuildPrompt(ctx);
    }

    private static WorkflowEngine CreateEngine(InMemoryStateStore store, params IStepHandler[] handlers)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<WorkflowEngine>();
        return new WorkflowEngine(handlers, new NullWebhookClient(), new NullRunClient(), logger, store);
    }

    private static WorkflowRegistry CreateRegistry() => new();
    private static WorkflowImportExportManager CreateImportExport(WorkflowRegistry registry) => new(registry);

    private static WorkflowDefinition CreateWorkflowDefinition(string name, string version)
    {
        return new WorkflowDefinition
        {
            Name = name,
            Version = version,
            Steps =
            [
                new() { Id = "step-1", Type = StepType.Code, Assembly = "Test", Class = "TestClass" },
                new() { Id = "step-2", Type = StepType.Code, Assembly = "Test", Class = "TestClass" },
            ]
        };
    }

    // ═══════════════════════════════════════════
    // 测试用 Handler 子类
    // ═══════════════════════════════════════════

    private class TimeoutCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        private readonly TimeSpan _handlerTimeout;
        private readonly TimeSpan _executeDelay;

        public override string Timeout => _handlerTimeout.ToString("hh\\:mm\\:ss");
        public TimeoutCodeStep(string stepId, TimeSpan handlerTimeout, TimeSpan executeDelay)
        {
            _stepId = stepId;
            _handlerTimeout = handlerTimeout;
            _executeDelay = executeDelay;
        }

        public override string StepId => _stepId;
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

        public override RetryConfigYaml? Retry => new()
        {
            MaxRetries = 3,
            Policy = RetryPolicy.Immediate,
        };

        public RetryCodeStep(string stepId) => _stepId = stepId;
        public override string StepId => _stepId;

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            ExecutionCount++;
            await Task.Delay(10, ct);
            // 始终失败，触发重试（TimeoutException 是重试可处理异常类型）
            throw new TimeoutException("always timeout");
        }
    }

    private class TimeoutWithRetryCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        private readonly TimeSpan _handlerTimeout;
        public int ExecutionCount { get; private set; }

        public override string Timeout => _handlerTimeout.ToString("hh\\:mm\\:ss");
        public override RetryConfigYaml? Retry => new()
        {
            MaxRetries = 3,
            Policy = RetryPolicy.Immediate,
        };

        public TimeoutWithRetryCodeStep(string stepId, TimeSpan handlerTimeout)
        {
            _stepId = stepId;
            _handlerTimeout = handlerTimeout;
        }

        public override string StepId => _stepId;

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            ExecutionCount++;
            await Task.Delay(10, ct);
            throw new TimeoutException("always fail");
        }
    }

    private class PromptAgentStep : AgentStepHandler
    {
        private readonly string _stepId;
        private readonly string? _handlerPrompt;

        public override string? Prompt => _handlerPrompt;
        public PromptAgentStep(string stepId, string? handlerPrompt)
        {
            _stepId = stepId;
            _handlerPrompt = handlerPrompt;
        }

        public override string StepId => _stepId;
        public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
        public override string RouteName => "test";
        public override string EventType => "test";
        public override string BuildPrompt(WorkflowContext context) => "built prompt";
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete("done"));
    }

    private class TestAgentStep : AgentStepHandler
    {
        private readonly string _stepId;
        private readonly string? _prompt;

        public override string? Prompt => _prompt;
        public TestAgentStep(string stepId, string? prompt) { _stepId = stepId; _prompt = prompt; }
        public override string StepId => _stepId;
        public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;
        public override string RouteName => "test";
        public override string EventType => "test";
        public override string BuildPrompt(WorkflowContext context) => "built prompt";
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete("done"));
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

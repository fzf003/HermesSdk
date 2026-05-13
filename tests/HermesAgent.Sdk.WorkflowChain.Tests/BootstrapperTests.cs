using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class BootstrapperTests
{
    // ═══════════════════════════════════════════
    // ApplyAsync: 将 Registry 中的定义同步到 Engine
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ApplyAsync_RegistersStepDefinitions_ToEngine()
    {
        var registry = new WorkflowRegistry();
        var engine = CreateEngine(new InMemoryStateStore());

        var definition = CreateWorkflowDefinition("test-wf", "1.0", "step-a");
        registry.Register(definition);

        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        Assert.False(bootstrapper.IsApplied("test-wf"));

        await bootstrapper.ApplyAsync("test-wf");

        Assert.True(bootstrapper.IsApplied("test-wf"));
    }

    // ═══════════════════════════════════════════
    // ApplyAllAsync: 同步所有已注册工作流
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ApplyAllAsync_AppliesAllRegisteredWorkflows()
    {
        var registry = new WorkflowRegistry();
        await using var engine = CreateEngine(new InMemoryStateStore());

        registry.Register(CreateWorkflowDefinition("wf-a", "1.0", "step-a"));
        registry.Register(CreateWorkflowDefinition("wf-b", "1.0", "step-b"));

        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        await bootstrapper.ApplyAllAsync();

        Assert.True(bootstrapper.IsApplied("wf-a"));
        Assert.True(bootstrapper.IsApplied("wf-b"));
    }

    // ═══════════════════════════════════════════
    // 重复 Apply: ReplaceStepDefinitions 语义
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ReApply_ReplacesSteps_WithoutError()
    {
        var registry = new WorkflowRegistry();
        await using var engine = CreateEngine(new InMemoryStateStore());

        registry.Register(CreateWorkflowDefinition("reapply-wf", "1.0", "step-a"));

        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        await bootstrapper.ApplyAsync("reapply-wf");
        await bootstrapper.ApplyAsync("reapply-wf"); // 第二次不抛异常

        Assert.True(bootstrapper.IsApplied("reapply-wf"));
    }

    // ═══════════════════════════════════════════
    // 错误处理: 工作流未注册时抛异常
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ApplyAsync_Throws_WhenWorkflowNotRegistered()
    {
        var registry = new WorkflowRegistry();
        await using var engine = CreateEngine(new InMemoryStateStore());

        var bootstrapper = new WorkflowBootstrapper(registry, engine);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            bootstrapper.ApplyAsync("nonexistent-wf"));
    }

    // ═══════════════════════════════════════════
    // IsApplied: 未 Apply 时返回 false
    // ═══════════════════════════════════════════

    [Fact]
    public void IsApplied_ReturnsFalse_WhenNotApplied()
    {
        var registry = new WorkflowRegistry();
        var engine = CreateEngine(new InMemoryStateStore());

        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        Assert.False(bootstrapper.IsApplied("anything"));
    }

    // ═══════════════════════════════════════════
    // 空 Registry: ApplyAllAsync 不抛异常
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ApplyAllAsync_NoRegisteredWorkflows_DoesNotThrow()
    {
        var registry = new WorkflowRegistry();
        await using var engine = CreateEngine(new InMemoryStateStore());

        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        await bootstrapper.ApplyAllAsync();
    }

    // ═══════════════════════════════════════════
    // RegisterThenApply: 先注册到 Registry，再同步到 Engine
    // ═══════════════════════════════════════════

    [Fact]
    public async Task RegisterThenApply_WithYamlConfig_StepDefinitionsExist()
    {
        var store = new InMemoryStateStore();
        var step = new TestCodeStep("process-step");
        await using var engine = CreateEngine(store, step);
        var registry = new WorkflowRegistry();

        // 模拟 YAML 加载：构造定义注册到 Registry
        var definition = CreateWorkflowDefinition("e2e-wf", "1.0", "process-step");
        definition.Steps[0].Timeout = "00:00:30";
        registry.Register(definition);

        // runtime 阶段：bootstrapper 同步到 engine
        var bootstrapper = new WorkflowBootstrapper(registry, engine);
        await bootstrapper.ApplyAsync("e2e-wf");

        Assert.True(bootstrapper.IsApplied("e2e-wf"));

        // 验证 Engine 可以接受该工作流名称启动（步骤定义已注册）
        var ctx = new WorkflowContext { InstanceId = "bt-e2e-sync" };
        var instanceId = await engine.StartAsync("process-step", ctx, CancellationToken.None, "e2e-wf");
        Assert.NotNull(instanceId);
    }

    // ═══════════════════════════════════════════
    // LoadAndApplyAsync: 一步加载 YAML 并同步到 Engine
    // ═══════════════════════════════════════════

    [Fact]
    public async Task LoadAndApplyAsync_ParsesRegistersAndApplies()
    {
        var store = new InMemoryStateStore();
        var step = new TestCodeStep("process-step");
        await using var engine = CreateEngine(store, step);
        var registry = new WorkflowRegistry();

        var bootstrapper = new WorkflowBootstrapper(registry, engine, new YamlWorkflowParser());

        const string yaml = @"
name: load-apply-test
version: '1.0'
steps:
  - id: process-step
    type: code
    assembly: TestAssembly
    class: TestCodeStep
    timeout: '00:00:30'
";

        var wfName = await bootstrapper.LoadAndApplyAsync(yaml);

        Assert.Equal("load-apply-test", wfName);
        Assert.True(bootstrapper.IsApplied(wfName));
    }

    [Fact]
    public async Task LoadAndApplyAsync_WithVersionOverride()
    {
        var store = new InMemoryStateStore();
        var step = new TestCodeStep("process-step");
        await using var engine = CreateEngine(store, step);
        var registry = new WorkflowRegistry();

        var bootstrapper = new WorkflowBootstrapper(registry, engine, new YamlWorkflowParser());

        const string yaml = @"
name: version-override-test
version: '1.0'
steps:
  - id: process-step
    type: code
    assembly: TestAssembly
    class: TestCodeStep
";

        var wfName = await bootstrapper.LoadAndApplyAsync(yaml, version: "2.0.0");

        Assert.Equal("version-override-test", wfName);
        var def = registry.Get(wfName);
        Assert.Equal("2.0.0", def.Version);
    }

    [Fact]
    public async Task LoadAndApplyFromFileAsync_LoadsFromFile()
    {
        var store = new InMemoryStateStore();
        var step = new TestCodeStep("file-step");
        await using var engine = CreateEngine(store, step);
        var registry = new WorkflowRegistry();

        var bootstrapper = new WorkflowBootstrapper(registry, engine, new YamlWorkflowParser());

        // 写入临时 YAML 文件
        var tempFile = Path.GetTempFileName() + ".yaml";
        try
        {
            await File.WriteAllTextAsync(tempFile, @"
name: file-load-test
version: '1.0'
steps:
  - id: file-step
    type: code
    assembly: TestAssembly
    class: TestCodeStep
    timeout: '00:01:00'
");
            var wfName = await bootstrapper.LoadAndApplyFromFileAsync(tempFile);

            Assert.Equal("file-load-test", wfName);
            Assert.True(bootstrapper.IsApplied(wfName));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
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

    private static WorkflowDefinition CreateWorkflowDefinition(string name, string version, params string[] stepIds)
    {
        return new WorkflowDefinition
        {
            Name = name,
            Version = version,
            Steps = stepIds.Select(id => new StepDefinition
            {
                Id = id,
                Type = StepType.Code,
                Class = id,
                Assembly = "TestAssembly"
            }).ToList()
        };
    }

    // ═══════════════════════════════════════════
    // 内嵌类型
    // ═══════════════════════════════════════════

    private sealed class TestCodeStep : CodeStepHandler
    {
        public override string StepId { get; }
        public Func<WorkflowContext, Task<StepResult>>? OnExecute { get; set; }

        public TestCodeStep(string stepId) => StepId = stepId;

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => OnExecute?.Invoke(context) ?? Task.FromResult(new StepResult { IsSuccess = true });
    }

    private sealed class NullWebhookClient : IHermesWebhookClient
    {
        public Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload,
            WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload,
            WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendDirectAsync(string routeName, string message,
            WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public void Dispose() { }
    }

    private sealed class NullRunClient : IHermesRunClient
    {
        public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult("run-mock");

        public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new RunEvent { Type = "run.completed", OutPut = "mock-output" };
        }

        public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RunResult { RunId = "run-mock", Status = "completed" });

        public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose() { }
    }
}

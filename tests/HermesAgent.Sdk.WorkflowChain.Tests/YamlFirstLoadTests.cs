using HermesAgent.Sdk.WorkflowChain.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

/// <summary>
/// Phase 7: YAML 优先加载集成测试。
/// 覆盖 tasks.md Phase 7 中 7.1–7.8 八个场景。
/// </summary>
public class YamlFirstLoadTests
{
    // ═══════════════════════════════════════════
    // 7.1: YAML 存在且 Name+Version 匹配 → 从 YAML 加载，跳过代码构建
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_YamlExistsAndMatches_LoadsFromYaml()
    {
        using var dir = new TempDir();
        var yamlPath = Path.Combine(dir.Path, "test-wf-1.0.yaml");
        File.WriteAllText(yamlPath, @"
id: yaml-loaded-id
name: test-wf
version: '1.0'
description: From YAML
steps:
  - id: step-a
    type: code
    class: StepA
    assembly: TestAssembly
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("yaml-loaded-id", def.Id);          // YAML 的 Id
        Assert.Equal("From YAML", def.Description);       // YAML 的 description
        Assert.Equal("test-wf", def.Name);
        Assert.Single(def.Steps);                         // YAML 有 1 个步骤
    }

    // ═══════════════════════════════════════════
    // 7.2: YAML Name 不匹配 → 告警 + 回退到代码构建
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_YamlNameMismatch_FallsBackToCodeBuild()
    {
        using var dir = new TempDir();
        var yamlPath = Path.Combine(dir.Path, "test-wf-1.0.yaml");
        File.WriteAllText(yamlPath, @"
id: yaml-id
name: wrong-name
version: '1.0'
steps:
  - id: step-a
    type: code
    class: StepA
    assembly: TestAssembly
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 代码构建的 Id
        Assert.Equal("test-wf", def.Name);       // 代码的 Name
    }

    [Fact]
    public void Register_YamlVersionMismatch_FallsBackToCodeBuild()
    {
        using var dir = new TempDir();
        var yamlPath = Path.Combine(dir.Path, "test-wf-1.0.yaml");
        File.WriteAllText(yamlPath, @"
id: yaml-id
name: test-wf
version: '9.9'
steps:
  - id: step-a
    type: code
    class: StepA
    assembly: TestAssembly
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 回退到代码构建
        Assert.Equal("1.0", def.Version);         // 代码的 Version
    }

    // ═══════════════════════════════════════════
    // 7.3: YAML 解析异常 → ERROR 日志 + 回退代码构建
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_YamlParseError_FallsBackToCodeBuild()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), ":- invalid yaml syntax");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 回退到代码构建
    }

    // ═══════════════════════════════════════════
    // 7.4: YAML 结构校验失败 → ERROR 日志 + 回退代码构建
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_YamlValidationFails_FallsBackToCodeBuild()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), @"
id: yaml-id
name: test-wf
version: '1.0'
steps: []
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 回退到代码构建
    }

    [Fact]
    public void Register_YamlMissingRequiredFields_FallsBackToCodeBuild()
    {
        using var dir = new TempDir();
        // Code 步骤缺少 class 和 assembly 字段 → Validate() 失败
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), @"
name: test-wf
version: '1.0'
steps:
  - id: step-a
    type: code
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 回退到代码构建
    }

    // ═══════════════════════════════════════════
    // 7.5: YAML 中有步骤无对应 handler → WARN 日志 + 仍加载 YAML
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_YamlHasStepsWithoutHandler_StillLoadsYaml()
    {
        using var dir = new TempDir();
        // YAML 比代码多一个步骤 "unknown-step"，找不到 handler
        // Handler 存在性校验是 WARN-only，不阻断加载
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), @"
id: yaml-loaded-id
name: test-wf
version: '1.0'
steps:
  - id: step-a
    type: code
    class: StepA
    assembly: TestAssembly
  - id: unknown-step
    type: code
    class: UnknownStep
    assembly: UnknownAssembly
");

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("yaml-loaded-id", def.Id);  // YAML 仍被加载
        Assert.Equal(2, def.Steps.Count);         // 两个步骤都在
    }

    // ═══════════════════════════════════════════
    // 7.6: 无论从哪加载，最终 WorkflowDefinition 均注册到 Registry
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Register_YamlPath_RegistersInRegistry()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), @"
id: reg-test-yaml
name: test-wf
version: '1.0'
steps:
  - id: step-a
    type: code
    class: StepA
    assembly: TestAssembly
");

        using var host = CreateHost(dir.Path);
        var registry = host.Services.GetRequiredService<WorkflowRegistry>();

        var def = registry.Get("test-wf");
        Assert.NotNull(def);
        Assert.Equal("reg-test-yaml", def.Id);   // YAML Id
    }

    [Fact]
    public async Task Register_CodePath_RegistersInRegistry()
    {
        using var dir = new TempDir();
        // YAML 不存在 → 走代码构建
        using var host = CreateHost(dir.Path);
        var registry = host.Services.GetRequiredService<WorkflowRegistry>();

        var def = registry.Get("test-wf");
        Assert.NotNull(def);
        Assert.Equal("code-built-id", def.Id);   // 代码构建 Id
    }

    // ═══════════════════════════════════════════
    // 7.7: AutoExportEnabled=false 时跳过导出
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AutoExportDisabled_DoesNotExportYaml()
    {
        using var dir = new TempDir();

        using var host = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                services.AddSingleton<IHermesRunClient, NullRunClient>();
                services.AddWorkflowChain(chain =>
                {
                    chain.Options.YamlConfigDirectory = dir.Path;
                    chain.Options.AutoExportEnabled = false;  // 关闭自动导出
                    chain.Register<TestWorkflow>();
                }, new InMemoryStateStore());
            })
            .Build();

        await host.StartAsync();

        // 启动后目录应没有 YAML 文件（因为 AutoExportEnabled=false）
        Assert.False(Directory.Exists(dir.Path) && Directory.GetFiles(dir.Path, "*.yaml").Length > 0,
            "AutoExportEnabled=false 时不应导出 YAML 文件");
    }

    // ═══════════════════════════════════════════
    // 7.8: 引擎 Id 索引正常工作
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Engine_StartsAndIndexesByYamlId()
    {
        using var dir = new TempDir();
        // YAML 中有单个 step-a，代码中注册对应的 handler
        File.WriteAllText(Path.Combine(dir.Path, "test-wf-1.0.yaml"), @"
id: yaml-index-id
name: test-wf
version: '1.0'
steps:
  - id: step-a
    type: code
    class: TestCodeStep
    assembly: TestAssembly
");

        // YAML path 跳过 BuildDefinition→handler 不注册，需要显式注册 handler
        using var host = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                services.AddSingleton<IHermesRunClient, NullRunClient>();
                services.AddWorkflowChain(chain =>
                {
                    chain.Options.YamlConfigDirectory = dir.Path;
                    chain.AddStep(new TestCodeStep("step-a"));
                    chain.Register<TestWorkflow>();
                }, new InMemoryStateStore());
            })
            .Build();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        await bootstrapper.ApplyAllAsync();

        var ctx = new WorkflowContext { InstanceId = "id-index-test" };
        var instanceId = await engine.StartAsync("step-a", ctx, CancellationToken.None, "test-wf");

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("id-index-test", instance.Context.InstanceId);
    }

    [Fact]
    public async Task Register_CodePath_DefinitionId_IsCodeBuiltId()
    {
        using var dir = new TempDir();
        // 无 YAML 文件 → 走代码构建，验证 Id 是 workflow.Id
        using var host = CreateHost(dir.Path);
        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        var def = registry.Get("test-wf");
        Assert.Equal("code-built-id", def.Id);
    }

    // ═══════════════════════════════════════════
    // 边界: YAML 文件不在配置目录中
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_NoYamlFile_BuildsFromCode()
    {
        using var dir = new TempDir();
        // 目录为空，没有 YAML 文件

        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Options.YamlConfigDirectory = dir.Path;
        builder.Register<TestWorkflow>();

        var def = Assert.Single(builder.GetWorkflowDefinitions());
        Assert.Equal("code-built-id", def.Id);   // 代码构建
    }

    // ═══════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════

    /// <summary>创建完整 DI 宿主（不含引擎步骤 handler）。用于 Registry 验证。</summary>
    private static IHost CreateHost(string yamlConfigDir)
    {
        return new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                services.AddSingleton<IHermesRunClient, NullRunClient>();
                services.AddWorkflowChain(chain =>
                {
                    chain.Options.YamlConfigDirectory = yamlConfigDir;
                    chain.Register<TestWorkflow>();
                }, new InMemoryStateStore());
            })
            .Build();
    }

    // ═══════════════════════════════════════════
    // 测试用 Workflow 定义（代码构建时 Id 固定为 "code-built-id"）
    // ═══════════════════════════════════════════

    private sealed class TestWorkflow : Workflow
    {
        public override string Name => "test-wf";
        public override string Id => "code-built-id";
        public override string Version => "1.0";

        protected internal override void Build(IStepBuilder builder)
        {
            builder.AddCodeStep("step-a", async (ctx, ct) =>
            {
                await Task.CompletedTask;
                return StepHandlerBaseExposed.Complete("from-code");
            });
        }
    }

    private sealed class TestCodeStep : CodeStepHandler
    {
        private readonly string _stepId;
        public TestCodeStep(string stepId) => _stepId = stepId;
        public override string StepId => _stepId;

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(StepHandlerBaseExposed.Complete("handler-done"));
    }

    // ═══════════════════════════════════════════
    // 内嵌辅助类型
    // ═══════════════════════════════════════════

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
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

    private sealed class InMemoryStateStore : IWorkflowStateStore
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

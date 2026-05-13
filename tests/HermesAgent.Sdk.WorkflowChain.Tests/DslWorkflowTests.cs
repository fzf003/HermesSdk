using HermesAgent.Sdk.WorkflowChain.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class DslWorkflowTests
{
    // ═══════════════════════════════════════════
    // DslCodeStepBuilder / DslAgentStepBuilder
    // ═══════════════════════════════════════════

    [Fact]
    public void DslCodeStepBuilder_BuildsDefaults()
    {
        var builder = new DslCodeStepBuilder();
        builder.WithTimeout("00:00:10")
               .WithTimeoutAction(TimeoutAction.Fail)
               .WithRetry(r => r.Immediate(3))
               .WithErrorPolicy(ErrorPolicy.SkipFailedBranch);

        var defaults = builder.BuildDefaults();

        Assert.Equal("00:00:10", defaults.Timeout);
        Assert.Equal("fail", defaults.TimeoutAction);
        Assert.NotNull(defaults.Retry);
        Assert.Equal(3, defaults.Retry!.MaxRetries);
        Assert.Equal("skip_failed_branch", defaults.ErrorPolicy);
    }

    [Fact]
    public void DslAgentStepBuilder_BuildsWithAgentFields()
    {
        var builder = new DslAgentStepBuilder();
        builder.WithTimeout("00:00:30");
        builder.WithPrompt("请分析数据");
        builder.WithSystemPrompt("你是助手");
        builder.WithRouteName("test.route");
        builder.WithEventType("test.event");
        builder.WithHeartbeatExtension("00:01:00");

        var defaults = builder.BuildDefaults();

        Assert.Equal("00:00:30", defaults.Timeout);
        Assert.Equal("请分析数据", defaults.Prompt);
        Assert.Equal("你是助手", defaults.SystemPrompt);
        Assert.Equal("test.route", defaults.RouteName);
        Assert.Equal("test.event", defaults.EventType);
        Assert.Equal("00:01:00", defaults.HeartbeatExtension);
    }

    [Fact]
    public void DslCodeStepBuilder_WithoutConfigure_DefaultsNull()
    {
        var builder = new DslCodeStepBuilder();
        var defaults = builder.BuildDefaults();

        Assert.Null(defaults.Timeout);
        Assert.Null(defaults.TimeoutAction);
        Assert.Null(defaults.Retry);
        Assert.Null(defaults.ErrorPolicy);
    }

    [Fact]
    public void DslAgentStepBuilder_DefaultsNull()
    {
        var builder = new DslAgentStepBuilder();
        var defaults = builder.BuildDefaults();

        Assert.Null(defaults.Timeout);
        Assert.Null(defaults.Prompt);
        Assert.Null(defaults.SystemPrompt);
    }

    // ═══════════════════════════════════════════
    // DslCodeStepBuilder WithName
    // ═══════════════════════════════════════════

    [Fact]
    public void DslCodeStepBuilder_WithName_StoresName()
    {
        var builder = new DslCodeStepBuilder();
        builder.WithName("步骤名称");

        Assert.Equal("步骤名称", builder.Name);
    }

    // ═══════════════════════════════════════════
    // AnonymousCodeStepHandler
    // ═══════════════════════════════════════════

    [Fact]
    public async Task AnonymousCodeStepHandler_ExecutesLambda()
    {
        var handler = new AnonymousCodeStepHandler("test-step", async (ctx, ct) =>
        {
            await Task.Delay(1, ct);
            return StepHandlerBaseExposed.Complete("done");
        });

        Assert.Equal("test-step", handler.StepId);

        var ctx = new WorkflowContext { InstanceId = "test" };
        var result = await handler.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.NextStepIds ?? []);
    }

    [Fact]
    public async Task AnonymousCodeStepHandler_ReturnsSequential()
    {
        var handler = new AnonymousCodeStepHandler("step-a", (ctx, ct) =>
        {
            return Task.FromResult(StepHandlerBaseExposed.Sequential("step-b", "output"));
        });

        var ctx = new WorkflowContext { InstanceId = "test" };
        var result = await handler.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["step-b"], result.NextStepIds);
        Assert.Equal("output", result.Output);
    }

    [Fact]
    public async Task AnonymousCodeStepHandler_FluentDefaults_OverrideVirtualProps()
    {
        var defaults = new StepHandlerDefaults
        {
            Timeout = "00:00:10",
            ErrorPolicy = "skip_failed_branch",
        };

        var handler = new AnonymousCodeStepHandler("test-step",
            (ctx, ct) => Task.FromResult(StepHandlerBaseExposed.Complete()),
            defaults);

        Assert.Equal("00:00:10", handler.Timeout);
        Assert.Equal("skip_failed_branch", handler.ErrorPolicy);
    }

    // ═══════════════════════════════════════════
    // AnonymousAgentStepHandler
    // ═══════════════════════════════════════════

    [Fact]
    public void AnonymousAgentStepHandler_BuildPrompt_UsesConfigFactory()
    {
        var handler = new AnonymousAgentStepHandler("agent-step",
            ctx => new AgentConfig
            {
                SystemPrompt = "系统提示",
                UserPrompt = $"用户提问: {ctx.InstanceId}"
            });

        var ctx = new WorkflowContext { InstanceId = "wf-001" };
        var prompt = handler.BuildPrompt(ctx);

        Assert.Equal("用户提问: wf-001", prompt);
        // SystemPrompt 来自 AgentConfig lambda，仅用于 BuildPrompt，不暴露为 handler 属性
        Assert.Null(handler.SystemPrompt);
    }

    [Fact]
    public void AnonymousAgentStepHandler_BuildPrompt_FallsBackToPromptProperty()
    {
        var defaults = new StepHandlerDefaults { Prompt = "默认提示词" };
        var handler = new AnonymousAgentStepHandler("agent-step", null, defaults);

        var ctx = new WorkflowContext { InstanceId = "test" };
        var prompt = handler.BuildPrompt(ctx);

        Assert.Equal("默认提示词", prompt);
    }

    [Fact]
    public void AnonymousAgentStepHandler_FluentDefaults_OverrideVirtualProps()
    {
        var defaults = new StepHandlerDefaults
        {
            Timeout = "00:05:00",
            Prompt = "测试提示",
            SystemPrompt = "测试系统提示",
            RouteName = "my.route",
            EventType = "my.event",
        };

        var handler = new AnonymousAgentStepHandler("agent-step", null, defaults);

        Assert.Equal("00:05:00", handler.Timeout);
        Assert.Equal("测试提示", handler.Prompt);
        Assert.Equal("测试系统提示", handler.SystemPrompt);
        Assert.Equal("my.route", handler.RouteName);
        Assert.Equal("my.event", handler.EventType);
    }

    [Fact]
    public async Task AnonymousAgentStepHandler_ExecuteAsync_ReturnsComplete()
    {
        var handler = new AnonymousAgentStepHandler("agent", null);
        var ctx = new WorkflowContext { InstanceId = "test" };

        var result = await handler.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ═══════════════════════════════════════════
    // Register<T> 集成测试（通过 DI 构建）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Register_SimpleWorkflow_ExecutesSuccessfully()
    {
        using var host = CreateDslHost<SimpleDslWorkflow>();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        await bootstrapper.ApplyAllAsync();

        var ctx = new WorkflowContext { InstanceId = "dsl-simple" };
        var instanceId = await engine.StartAsync("step-a", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("completed", instance.Status);

        var records = engine.GetStepRecords(instanceId);
        Assert.Contains(records, r => r.StepId == "step-a" && r.Status == StepStatus.Completed);
        Assert.Contains(records, r => r.StepId == "step-b" && r.Status == StepStatus.Completed);
    }

    [Fact]
    public async Task Register_Workflow_RegisteredInRegistry()
    {
        using var host = CreateDslHost<SimpleDslWorkflow>();

        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        var definition = registry.Get("simple-dsl");

        Assert.NotNull(definition);
        Assert.Equal("simple-dsl", definition.Name);
        Assert.Equal("1.0", definition.Version);
        Assert.Equal(2, definition.Steps.Count);
        Assert.Equal("step-a", definition.Steps[0].Id);
        Assert.Equal("step-b", definition.Steps[1].Id);
    }

    [Fact]
    public async Task Register_FluentConfig_OverridesHandlerDefaults()
    {
        using var host = CreateDslHost<FluentConfigDslWorkflow>();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        await bootstrapper.ApplyAllAsync();

        var ctx = new WorkflowContext { InstanceId = "dsl-fluent" };
        var instanceId = await engine.StartAsync("entry", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("completed", instance.Status);
    }

    [Fact]
    public async Task Register_DuplicateWorkflowName_Throws()
    {
        var builder = new WorkflowChainBuilder(new ServiceCollection());
        builder.Register<SimpleDslWorkflow>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Register<SimpleDslWorkflow>());

        Assert.Contains("已通过 AddWorkflow 注册", ex.Message);
    }

    [Fact]
    public async Task Register_WithVersionAndDescription_Works()
    {
        using var host = CreateDslHost<VersionedDslWorkflow>();

        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        var definition = registry.Get("versioned-dsl");

        Assert.Equal("versioned-dsl", definition.Name);
        Assert.Equal("2.0", definition.Version);
        Assert.Equal("有版本描述的工作流", definition.Description);
    }

    [Fact]
    public async Task Register_MixedWithHandlerClass_Works()
    {
        using var host = CreateDslHost<MixedDslWorkflow>();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        await bootstrapper.ApplyAllAsync();

        var ctx = new WorkflowContext { InstanceId = "dsl-mixed" };
        var instanceId = await engine.StartAsync("handler-step", ctx, CancellationToken.None);

        var instance = engine.GetInstance(instanceId);
        Assert.NotNull(instance);
        Assert.Equal("completed", instance.Status);

        var records = engine.GetStepRecords(instanceId);
        Assert.Contains(records, r => r.StepId == "handler-step" && r.Status == StepStatus.Completed);
        Assert.Contains(records, r => r.StepId == "dsl-step" && r.Status == StepStatus.Completed);
    }

    // ═══════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════

    private static IHost CreateDslHost<TWorkflow>()
        where TWorkflow : Workflow, new()
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
                    chain.Register<TWorkflow>();
                }, new InMemoryStateStore());
            })
            .Build();
    }

    // ═══════════════════════════════════════════
    // 测试用 Workflow 定义
    // ═══════════════════════════════════════════

    private sealed class SimpleDslWorkflow : Workflow
    {
        public override string Name => "simple-dsl";

        protected internal override void Build(IStepBuilder builder)
        {
            builder.AddCodeStep("step-a", async (ctx, ct) =>
            {
                return StepHandlerBaseExposed.Sequential("step-b", "done");
            })
            .WithName("步骤A");

            builder.AddCodeStep("step-b", (ctx, ct) =>
            {
                return Task.FromResult(StepHandlerBaseExposed.Complete("all done"));
            })
            .WithName("步骤B");
        }
    }

    private sealed class FluentConfigDslWorkflow : Workflow
    {
        public override string Name => "fluent-dsl";

        protected internal override void Build(IStepBuilder builder)
        {
            builder.AddCodeStep("entry", async (ctx, ct) =>
            {
                return StepHandlerBaseExposed.Complete("ok");
            })
            .WithTimeout("00:00:30")
            .WithName("入口")
            .WithRetry(r => r.Immediate(2));
        }
    }

    private sealed class VersionedDslWorkflow : Workflow
    {
        public override string Name => "versioned-dsl";
        public override string Version => "2.0";
        public override string? Description => "有版本描述的工作流";

        protected internal override void Build(IStepBuilder builder)
        {
            builder.AddCodeStep("only-step", (ctx, ct) =>
                Task.FromResult(StepHandlerBaseExposed.Complete()));
        }
    }

    private sealed class MixedDslWorkflow : Workflow
    {
        public override string Name => "mixed-dsl";

        protected internal override void Build(IStepBuilder builder)
        {
            // 引用现有的 Handler 类
            builder.AddCodeStep<ExistingDslHandler>();
            // 内联 Lambda
            builder.AddCodeStep("dsl-step", (ctx, ct) =>
            {
                var output = ctx.GetOutput<string>("handler-step");
                return Task.FromResult(StepHandlerBaseExposed.Complete(output));
            });
        }
    }

    /// <summary>供混合模式测试用的现有 Handler 类</summary>
    private sealed class ExistingDslHandler : CodeStepHandler
    {
        public override string StepId => "handler-step";

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            return Task.FromResult(StepHandlerBaseExposed.Sequential("dsl-step", "from-handler"));
        }
    }

    /// <summary>公开 StepHandlerBase 受保护的工厂方法供测试使用</summary>
    private abstract class StepHandlerBaseExposed : StepHandlerBase
    {
        public static new StepResult Sequential(string nextStepId, object? output = null)
            => StepHandlerBase.Sequential(nextStepId, output);

        public static new StepResult Complete(object? output = null)
            => StepHandlerBase.Complete(output);

        public static new StepResult Failed(Exception ex)
            => StepHandlerBase.Failed(ex);
    }

    /// <summary>测试用 Null 客户端（与现有测试共享模式）</summary>
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

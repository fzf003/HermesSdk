using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HermesAgent.Sdk;
using HermesAgent.Sdk.WorkflowChain;

// ====================================================================
// WorkflowChain 阶段一演示 — 代码审查工作流（全 CodeStep，无需 Agent 运行）
//
// 拓扑:
//   fetch-code → static-analysis ─┬─→ summary → notify
//                 test-analysis  ──┘
//
// 关键特性演示：
//   1. CodeStep 同步执行
//   2. Parallel() 并行分叉
//   3. 手动 Join（Handler 内检查并行伙伴）
//   4. WorkflowContext 步骤间状态传递
//   5. GetTimelineSummary() 诊断时间线
// ====================================================================

// 注册 DI
var services = new ServiceCollection()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddSingleton<IHermesWebhookClient, DemoWebhookClient>()
    .AddSingleton<IHermesRunClient, DemoRunClient>()
    .AddWorkflowChain(chain =>
    {
        chain.AddStep<FetchCodeHandler>();
        chain.AddStep<StaticAnalysisHandler>();
        chain.AddStep<TestAnalysisHandler>();
        chain.AddStep<SummaryHandler>();
        chain.AddStep<NotifyHandler>();
    })
    .BuildServiceProvider();

var engine = services.GetRequiredService<WorkflowEngine>();

// 创建上下文并启动
var context = new WorkflowContext
{
    InitialInput = new Dictionary<string, object?>
    {
        ["repoUrl"] = "https://github.com/example/repo",
    },
};

Console.WriteLine("=== WorkflowChain 阶段一演示 ===");
Console.WriteLine($"启动工作流: {context.InstanceId}\n");

var instanceId = await engine.StartAsync(CodeReviewWorkflow.EntryStepId, context);

// 等待完成（所有步骤都是 CodeStep，会同步执行完成）
await Task.Delay(5000);

// 输出时间线
var instance = engine.GetInstance(instanceId);
Console.WriteLine($"\n工作流状态: {instance?.Status}");
Console.WriteLine(engine.GetTimelineSummary(instanceId));

Console.WriteLine("\n=== 演示完成 ===");

// ====================================================================
// 工作流定义
// ====================================================================
public static class CodeReviewWorkflow
{
    public const string EntryStepId = "fetch-code";
}

// ====================================================================
// Step 1: 拉取代码
// ====================================================================
public class FetchCodeHandler : CodeStepHandler
{
    public override string StepId => "fetch-code";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var repoUrl = context.GetData<string>("repoUrl") ?? "(未知仓库)";
        Console.WriteLine($"[{StepId}] 拉取代码: {repoUrl}");

        await Task.Delay(500, ct); // 模拟网络延迟
        var code = $"// 来自 {repoUrl} 的代码\npublic class OrderService {{ /* ... */ }}";

        context.StepOutputs[StepId] = code;

        // 并行启动 static-analysis 和 test-analysis
        return Parallel("static-analysis", "test-analysis");
    }
}

// ====================================================================
// Step 2: 静态分析
// ====================================================================
public class StaticAnalysisHandler : CodeStepHandler
{
    public override string StepId => "static-analysis";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var code = context.GetOutput<string>("fetch-code") ?? "";
        Console.WriteLine($"[{StepId}] 执行静态分析...");

        await Task.Delay(800, ct); // 模拟分析耗时
        var result = "静态分析结果: 代码质量 A 级, 无安全漏洞";

        context.StepOutputs[StepId] = result;

        // 检查并行伙伴 test-analysis 是否完成
        if (context.StepOutputs.ContainsKey("test-analysis"))
            return Sequential("summary");

        // 等待伙伴完成
        return new StepResult
        {
            StepId = StepId,
            IsSuccess = true,
            WaitForParallelCompletion = true,
        };
    }
}

// ====================================================================
// Step 3: 测试分析
// ====================================================================
public class TestAnalysisHandler : CodeStepHandler
{
    public override string StepId => "test-analysis";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var code = context.GetOutput<string>("fetch-code") ?? "";
        Console.WriteLine($"[{StepId}] 执行测试分析...");

        await Task.Delay(600, ct); // 模拟分析耗时
        var result = "测试分析结果: 覆盖率 85%, 缺少边界测试";

        context.StepOutputs[StepId] = result;

        // 检查并行伙伴 static-analysis 是否完成
        if (context.StepOutputs.ContainsKey("static-analysis"))
            return Sequential("summary");

        return new StepResult
        {
            StepId = StepId,
            IsSuccess = true,
            WaitForParallelCompletion = true,
        };
    }
}

// ====================================================================
// Step 4: 汇总
// ====================================================================
public class SummaryHandler : CodeStepHandler
{
    public override string StepId => "summary";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var staticResult = context.GetOutput<string>("static-analysis") ?? "(无)";
        var testResult = context.GetOutput<string>("test-analysis") ?? "(无)";

        Console.WriteLine($"[{StepId}] 生成审查报告...");

        var summary = $"## 审查报告\n\n### 静态分析\n{staticResult}\n\n### 测试分析\n{testResult}";
        context.StepOutputs[StepId] = summary;

        return Sequential("notify");
    }
}

// ====================================================================
// Step 5: 通知
// ====================================================================
public class NotifyHandler : CodeStepHandler
{
    private readonly ILogger<NotifyHandler> _logger;

    public override string StepId => "notify";

    public NotifyHandler(ILogger<NotifyHandler> logger)
    {
        _logger = logger;
    }

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var summary = context.GetOutput<string>("summary") ?? "";
        _logger.LogInformation("发送审查报告:\n{Summary}", summary);

        return Complete(summary);
    }
}

// ====================================================================
// Demo Stub — 无需真实 Hermes Agent 运行
// ====================================================================

public class DemoWebhookClient : IHermesWebhookClient
{
    public Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload, WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload, WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public Task<WebhookSendResult> SendDirectAsync(string routeName, string message, WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public void Dispose() { }
}

public class DemoRunClient : IHermesRunClient
{
    public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult("demo-run-id");

    public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new RunEvent { Type = "RunCompleted", Data = new Dictionary<string, object?> { ["result"] = "demo-result" } };
        await Task.CompletedTask;
    }

    public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RunResult());

    public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose() { }
}

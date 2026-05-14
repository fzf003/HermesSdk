using System.Text.Json;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// WorkflowChainDemo — 现代配置模式演示：
///   • AddCodeStep / AddAgentStep Fluent API 注册
///   • IWorkflowBootstrapper 运行时同步
///   • SQLite 持久化 + 心跳检测
///   • Agent + Code 混合多步骤编排
/// </summary>
class Program
{
    private const string DbPath = "workflow-demo.db";

    static async Task Main(string[] _)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CleanupDatabase();

        using var host = CreateHostBuilder().Build();
        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        var importExport = host.Services.GetRequiredService<WorkflowImportExportManager>();

        // 将已注册的工作流同步到 Engine 运行时
        await bootstrapper.ApplyAllAsync();
        await host.StartAsync();

        // =================================================================
        // Phase 1 — 启动工作流
        // =================================================================
        PrintHeader("Phase 1 — 启动工作流");

        var registry = host.Services.GetRequiredService<WorkflowRegistry>();

        var context = new WorkflowContext
        {
            InitialInput = new Dictionary<string, object?>
            {
                ["title"] = "采购申请 #P2026-0429",
                ["amount"] = 15000,
                ["department"] = "研发部",
                ["requester"] = "张三",
            },
        };

        var instanceId = await engine.StartWorkflowAsync("review-workflow", context, registry);
        PrintSection($"启动工作流: {instanceId}");
        Console.WriteLine($"  状态: {engine.GetInstance(instanceId)?.Status}");

        // =================================================================
        // Phase 2 — 检查执行状态
        // =================================================================
        PrintHeader("Phase 2 — 检查执行状态");

        var instance = engine.GetInstance(instanceId)!;
        PrintInstance(instance);

        var records = engine.GetStepRecords(instanceId);
        Console.WriteLine($"  步骤执行档案 ({records.Count} 条):");
        foreach (var r in records)
        {
            Console.WriteLine(
                $"    [{r.Status,-12}] {r.StepId,-20} {r.StepType,-8} "
                    + $"{(r.StartedAt != default ? r.StartedAt.ToString("HH:mm:ss") : "-")}  "
                    + $"{(r.ErrorMessage != null ? $"err: {r.ErrorMessage}" : "")}"
            );
        }

        // =================================================================
        // Phase 3 — 最终结果
        // =================================================================
        PrintHeader("Phase 3 — 最终结果");

        instance = engine.GetInstance(instanceId)!;
        PrintInstance(instance);
        Console.WriteLine("  步骤时间线:");
        foreach (var r in engine.GetStepRecords(instanceId))
        {
            Console.WriteLine(
                $"    [{r.Status,-12}] {r.StepId,-20} {r.StepType,-8} "
                    + $"duration={r.Duration?.TotalMilliseconds:F0}ms  "
                    + $"{(r.OutputSnapshot != null ? $"output: {Truncate(r.OutputSnapshot, 50)}" : "")}"
            );
        }

        // =================================================================
        // 清理
        // =================================================================
        CleanupDatabase();
        PrintSection("✅ 演示完成");
        Console.ReadKey(true);
        await host.StopAsync();
    }

    // ================================================================
    // Host Builder — 现代配置模式
    // ================================================================
    static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(c =>
                {
                    c.IncludeScopes = true;
                    c.SingleLine = true;
                    c.TimestampFormat = "HH:mm:ss ";
                });
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices((context, services) =>
            {
                services
                    .AddHermesAgent(context.Configuration)
                    .AddWorkflowChain(chain =>
                    {

                        // SQLite 持久化 → 支持重启恢复
                        chain.AddSqliteStateStore($"Data Source={DbPath}");

                        // 心跳超时 30 秒（演示用）
                        chain.SetHeartbeatThreshold(TimeSpan.FromSeconds(30));

                        // AddWorkflow 注册 — 通过 Fluent API 配置默认策略
                        // 优先级: YAML > Fluent 配置 > Handler 虚属性 > 引擎内建
                        chain.AddWorkflow(opt => opt
                            .AddAgentStep<ReviewAgentStep>(c => c
                                .WithTimeout("00:05:00")
                            )
                            .AddCodeStep<ApproveCodeStep>(c => c
                                .WithTimeout("00:00:30")
                                .WithRetry(r => r.Immediate(3))
                            )
                            .AddCodeStep<NotifyCodeStep>(c => c
                                .WithTimeout("00:00:10")
                            )
                        ).WithName("review-workflow")
                        .WithVersion("1.0")
                        .WithDescription("采购审批流程，包含一个Agent步骤和两个Code步骤");


                    });

                // Null 客户端 — 无需真实 Hermes Server
                services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                services.AddSingleton<IHermesRunClient, NullRunClient>();
            });

    // ================================================================
    // 步骤处理器
    // ================================================================

    /// <summary>审核 Agent 步骤 — 发送给外部 Agent 审查</summary>
    private sealed class ReviewAgentStep : AgentStepHandler
    {
        public override string StepId => "review-agent";
        public override string RouteName => "workflow.review";
        public override string EventType => "workflow.step";
        public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;

        public override string BuildPrompt(WorkflowContext context)
        {
            var title = context.InitialInput["title"];
            var amount = context.InitialInput["amount"];

            var append =
                "只用Json回复,随机true or false\r\n{\r\n\"Success\":true, //true or false\r\n\"Message\":\"\" // 说明原因\r\n}, 回复格式:json";

            var apply = $"请审核以下采购申请:\n标题: {title}\n金额: {amount} 元";

            return apply + $"\r\n {append}";
        }

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            if (context.StepOutputs.TryGetValue(StepId, out var output))
            {
                var outputjson = output
                    ?.ToString()
                    ?.Replace("```json", string.Empty)
                    .Replace("```", string.Empty);
                if (string.IsNullOrWhiteSpace(outputjson))
                    return Failed(new Exception("消息为空"));

                var outmessage = JsonSerializer.Deserialize<OutMessage>(outputjson);
                if (outmessage?.Success is true)
                {
                    context.SetData(StepId, outmessage);
                    return Sequential("approve-code", outmessage);
                }
            }

            return Failed(new Exception("审核未通过"));
        }
    }

    /// <summary>审批 Code 步骤 — 本地业务逻辑</summary>
    private sealed class ApproveCodeStep : CodeStepHandler
    {
        public override string StepId => "approve-code";

        readonly ILogger<ApproveCodeStep> _logger;

        public ApproveCodeStep(ILogger<ApproveCodeStep> logger)
        {
            _logger = logger;
        }

        public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            var reviewOutput = context.GetOutput<string>("review-agent");
            Console.WriteLine($"  [CodeStep] 读取 review-agent 输出: {reviewOutput}");
            this._logger.LogInformation($"  [CodeStep] 读取 review-agent 输出: {reviewOutput}");

            context.StepOutputs[StepId] = new { decision = "approved", finalAmount = 15000 };
            return Sequential("notify-code", context.StepOutputs[StepId]);
        }
    }

    /// <summary>通知 Code 步骤 — 执行完成通知</summary>
    private sealed class NotifyCodeStep : CodeStepHandler
    {
        public override string StepId => "notify-code";

        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        {
            Console.WriteLine("  [NotifyCodeStep] 通知步骤执行完成");
            return Task.FromResult(Complete(new
            {
                Success = true,
                Message = "通知已发送"
            }));
        }
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 64));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 64));
    }

    private static void PrintSection(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  ▶ {text}");
    }

    private static void PrintInstance(WorkflowInstance instance)
    {
        var inFlightIds = instance.GetStepRecords()
            .Where(r => r.Status is StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running)
            .Select(r => r.StepId)
            .ToList();

        Console.WriteLine($"  实例 ID:    {instance.Context.InstanceId}");
        Console.WriteLine($"  状态:       {instance.Status}");
        Console.WriteLine($"  入口步骤:   {instance.EntryStepId}");
        Console.WriteLine($"  IsRunning:  {instance.Context.IsRunning}");
        Console.WriteLine($"  活跃步骤:   [{string.Join(", ", inFlightIds)}]");
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..(maxLen - 3)] + "...";

    private static void CleanupDatabase()
    {
        try { if (File.Exists(DbPath)) File.Delete(DbPath); }
        catch { /* ignore */ }
    }

    public record OutMessage
    {
        public string? Message { get; init; }
        public bool Success { get; init; }
    }
}

// =====================================================================
// Mock 客户端 — 用于独立运行 WorkflowEngine 无需真实 Hermes Server
// =====================================================================

internal sealed class NullWebhookClient : IHermesWebhookClient
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

internal sealed class NullRunClient : IHermesRunClient
{
    public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult("run-mock");

    public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new RunEvent
        {
            Type = "run.completed",
            OutPut = """{"Success":true,"Message":"审核通过"}"""
        };
    }

    public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RunResult { RunId = "run-mock", Status = "completed" });

    public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose() { }
}

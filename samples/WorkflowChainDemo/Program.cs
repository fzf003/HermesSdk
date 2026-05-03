using System.Text.Json;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static HermesAgent.Sdk.WorkflowChain.Demo.Program;

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// WorkflowChainDemo — 演示 Phase 2 所有核心功能：
///   • SQLite 持久化存储（重启恢复）
///   • 心跳检测 + 超时标记
///   • 多步骤工作流编排（Agent + Code 混合）
///   • Webhook 回调模拟
///   • 状态检查 API（GetInstance / GetStepRecords）
/// </summary>
class Program
{
    private const string DbPath = "workflow-demo.db";

    static async Task Main(string[] _)
    {
        // ---- 清理上次运行残留 ----
        CleanupDatabase();

        // =================================================================
        // Phase 1 — 构建 Host，注册 WorkflowChain
        // =================================================================
        PrintHeader("Phase 1 — 构建 Host 与 DI 注册");

        using var host = Host.CreateDefaultBuilder()
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
            .ConfigureServices(
                (context, services) =>
                {
                    services
                        .AddHermesAgent(context.Configuration)
                        .AddWorkflowChain(chain =>
                        {
                            // 使用 SQLite 持久化 → 支持重启恢复
                            chain.AddSqliteStateStore($"Data Source={DbPath}");

                            // 心跳超时设为 30 秒（演示用，生产建议 5 分钟）
                            chain.SetHeartbeatThreshold(TimeSpan.FromSeconds(30));

                            // 注册 3 个步骤：Agent → Code → Agent
                            chain.AddStep<ReviewAgentStep>();
                            chain.AddStep<ApproveCodeStep>();
                            chain.AddStep<NotifyAgentStep>();
                        });
                }
            )
            .Build();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();

        // 启动 Host → 触发 WorkflowEngineInitializationService → InitializeAsync()
        PrintSection("启动 Host（触发 InitializeAsync 恢复运行中实例）");
        await host.StartAsync();

        // =================================================================
        // Phase 2 — 启动工作流
        // =================================================================
        PrintHeader("Phase 2 — 启动工作流");

        var context = new WorkflowContext
        {
            InstanceId = $"demo-{DateTime.Now:yyyyMMddHHmmss}",
            InitialInput = new Dictionary<string, object?>
            {
                ["title"] = "采购申请 #P2026-0429",
                ["amount"] = 15000,
                ["department"] = "研发部",
                ["requester"] = "张三",
            },
        };

        PrintSection($"启动工作流: {context.InstanceId}");
        var instanceId = await engine.StartAsync("review-agent", context);
        Console.WriteLine($"  工作流实例 ID: {instanceId}");
        Console.WriteLine($"  状态: {engine.GetInstance(instanceId)?.Status}");

        // =================================================================
        // Phase 3 — 检查执行状态
        // =================================================================
        PrintHeader("Phase 3 — 检查执行状态");

        var instance = engine.GetInstance(instanceId)!;
        PrintInstance(instance);

        var records = engine.GetStepRecords(instanceId);
        Console.WriteLine($"  步骤执行档案 ({records.Count} 条):");
        foreach (var r in records)
        {
            Console.WriteLine(
                $"    [{r.Status, -12}] {r.StepId, -20} {r.StepType, -8} "
                    + $"{(r.StartedAt != default ? r.StartedAt.ToString("HH:mm:ss") : "-")}  "
                    + $"{(r.ErrorMessage != null ? $"err: {r.ErrorMessage}" : "")}"
            );
        }

        // =================================================================
        // Phase 4 — 模拟 Agent Webhook 回调
        // =================================================================
        /* PrintHeader("Phase 4 — 模拟 Agent Webhook 回调");

           PrintSection("模拟 review-agent 回调: 审核通过 ✓");
           await engine.OnWebhookCallbackAsync(
               instanceId: instanceId,
               completedStepId: "review-agent",
               output: """{"approved":true,"comment":"预算合理，建议批准"}""",
               error: null,
               ct: CancellationToken.None);
       */

        // approve-code 应该是 CodeStep，已经在回调后自动执行了
        instance = engine.GetInstance(instanceId)!;
        Console.WriteLine($"  当前状态: {instance.Status}");
        var activeIds = instance
            .GetStepRecords()
            .Where(
                r =>
                    r.Status is StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running
            )
            .Select(r => r.StepId);
        Console.WriteLine($"  活跃步骤: [{string.Join(", ", activeIds)}]");

        /*  PrintSection("模拟 notify-agent 回调: 通知已发送 ✓");
          await engine.OnWebhookCallbackAsync(
              instanceId: instanceId,
              completedStepId: "notify-agent",
              output: """{"notificationSent":true,"channel":"email"}""",
              error: null,
              ct: CancellationToken.None);
        */


        instance = engine.GetInstance(instanceId)!;
        Console.WriteLine($"  最终状态: {instance.Status}");

        // =================================================================
        // Phase 5 — 检查最终结果
        // =================================================================
        PrintHeader("Phase 5 — 最终结果");

        PrintInstance(instance);
        Console.WriteLine("  步骤时间线:");
        foreach (var r in engine.GetStepRecords(instanceId))
        {
            Console.WriteLine(
                $"    [{r.Status, -12}] {r.StepId, -20} {r.StepType, -8} "
                    + $"duration={r.Duration?.TotalMilliseconds:F0}ms  "
                    + $"{(r.OutputSnapshot != null ? $"output: {Truncate(r.OutputSnapshot, 50)}" : "")}"
            );
        }

        /*
        // =================================================================
        // Phase 6 — 演示重启恢复
        // =================================================================
        PrintHeader("Phase 6 — 演示重启恢复");

        Console.WriteLine("  关闭 Host...");
        await host.StopAsync();

        // 注意：engine 是 singleton，同一 Host 生命周期内只有一份
        // 第二次 StartAsync 时 InitializeAsync 被 _initialized 标志阻止
        // 重新创建 Host 展示真正的重启恢复
        PrintSection("重新创建 Host（模拟重启）...");
        using var host2 = Host.CreateDefaultBuilder()
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
                services.AddHermesAgent(context.Configuration);

                services.AddWorkflowChain(chain =>
                {
                    chain.AddSqliteStateStore($"Data Source={DbPath}");
                    chain.SetHeartbeatThreshold(TimeSpan.FromSeconds(30));
                    chain.AddStep<ReviewAgentStep>();
                    chain.AddStep<ApproveCodeStep>();
                    chain.AddStep<NotifyAgentStep>();
                });
            })
            .Build();

        var engine2 = host2.Services.GetRequiredService<WorkflowEngine>();
        await host2.StartAsync();

        PrintSection("重启后检查状态:");
        var restoredInstance = engine2.GetInstance(instanceId);
        if (restoredInstance != null)
        {
            var restoredRecords = engine2.GetStepRecords(instanceId);
            var restoredActive = restoredRecords
                .Where(r => r.Status is StepStatus.Dispatched or StepStatus.Recovering);
            Console.WriteLine($"  实例 {instanceId}: 状态={restoredInstance.Status}, "
                + $"活跃步骤={restoredActive.Count()}");
        }
        else
        {
            Console.WriteLine($"  实例 {instanceId}: 已完成，未被恢复（符合预期）");
        }

        Console.WriteLine($"  引擎中实例总数: {GetInstanceCount(engine2)}");

        await host2.StopAsync();

        // 等待连接释放后再清理
        await Task.Delay(200);
        */

        // =================================================================
        // 清理
        // =================================================================
        try
        {
            CleanupDatabase();
        }
        catch
        { /* SQLite 文件可能仍被占用，忽略 */
        }
        PrintSection("✅ 演示完成");
    }

    // ================================================================
    // 步骤定义
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

            return apply + $"/r/n {append}";
        }

        public override async Task<StepResult> ExecuteAsync(
            WorkflowContext context,
            CancellationToken ct
        )
        {
            if (context.StepOutputs.TryGetValue(this.StepId, out var output))
            {
                var outputjson = output
                    ?.ToString()
                    ?.Replace("```json", string.Empty)
                    .Replace("```", string.Empty);
                if (string.IsNullOrWhiteSpace(outputjson))
                {
                    return Failed(new Exception("消息为空"));
                }

                var outmessage = JsonSerializer.Deserialize<OutMessage>(outputjson);

                if (outmessage.Success)
                {
                    context.SetData(StepId, outmessage);
                    return Sequential("approve-code", outmessage); //转下一步
                }
            }

            return Failed(new Exception());
        }
    }

    /// <summary>审批 Code 步骤 — 本地业务逻辑</summary>
    private sealed class ApproveCodeStep : CodeStepHandler
    {
        public override string StepId => "approve-code";

        public override async Task<StepResult> ExecuteAsync(
            WorkflowContext context,
            CancellationToken ct
        )
        {
            // 读取前置 Agent 步骤的输出
            var reviewOutput = context.GetOutput<string>("review-agent");

            Console.WriteLine($"  [CodeStep] 读取 review-agent 输出: {reviewOutput}");

            context.StepOutputs[StepId] = new { decision = "approved", finalAmount = 15000 };

            return Sequential("notify-agent", context.StepOutputs[StepId]);

            //return Failed(new Exception());
        }
    }

    /// <summary>通知 Agent 步骤 — 发送通知给下游系统</summary>
    private sealed class NotifyAgentStep : CodeStepHandler
    {
        public override string StepId => "notify-agent";

        //public override string RouteName => "workflow.notify";
        //public override string EventType => "workflow.step";

        /* public override string BuildPrompt(WorkflowContext context)
         {
             var decision = context.GetOutput<object>("approve-code");
             return $"审批完成，请发送通知。审批决策: {decision}";
         }*/  

        public override async Task<StepResult> ExecuteAsync(
            WorkflowContext context,
            CancellationToken ct
        )
        {
            Console.WriteLine($"  [notify-agent] 执行通知步骤");
            return Complete(new { 
             Sucess=true,
             Message="通知Agent已经执行完毕"
            });
        }
    }

    // ================================================================
    // 辅助工具
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
        var inFlightIds = instance
            .GetStepRecords()
            .Where(
                r =>
                    r.Status is StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running
            )
            .Select(r => r.StepId)
            .ToList();

        Console.WriteLine($"  实例 ID:   {instance.Context.InstanceId}");
        Console.WriteLine($"  状态:      {instance.Status}");
        Console.WriteLine($"  入口步骤:  {instance.EntryStepId}");
        Console.WriteLine($"  IsRunning:  {instance.Context.IsRunning}");
        Console.WriteLine($"  活跃步骤:   [{string.Join(", ", inFlightIds)}]");
    }

    private static int GetInstanceCount(WorkflowEngine engine)
    {
        // 通过反射获取私有字段 _instances 的计数（仅演示用）
        var field = typeof(WorkflowEngine).GetField(
            "_instances",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        if (field?.GetValue(engine) is System.Collections.IDictionary dict)
            return dict.Count;
        return -1;
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..(maxLen - 3)] + "...";

    private static void CleanupDatabase()
    {
        if (File.Exists(DbPath))
            File.Delete(DbPath);
    }

    public record OutMessage
    {
        public string Message { get; init; }

        public bool Success { get; init; }
    }
}

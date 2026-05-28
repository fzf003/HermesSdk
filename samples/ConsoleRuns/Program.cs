using HermesAgent.Sdk;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// ConsoleRuns 示例程序 — 演示 HermesAgent.Sdk Runs API 全部能力
///
/// 覆盖的 API:
///   StartAsync / GetRunStatusAsync / SubscribeEventsAsync
///   StopRunAsync / ApproveRunAsync / RunWithLoggingAsync
/// </summary>
class Program
{
    private const string DefaultPrompt = "分析 HermesAgent.Sdk 项目中可能存在的性能瓶颈";

    private static readonly Dictionary<string, (ConsoleColor Color, string Icon)> EventStyles = new()
    {
        ["run_started"] = (ConsoleColor.Cyan, "🚀"),
        ["tool.started"] = (ConsoleColor.Yellow, "🔧"),
        ["tool.completed"] = (ConsoleColor.Green, "✅"),
        ["reasoning.available"] = (ConsoleColor.Magenta, "💡"),
        ["message.delta"] = (ConsoleColor.White, "📝"),
        ["run.completed"] = (ConsoleColor.Green, "🎯"),
        ["run.failed"] = (ConsoleColor.Red, "❌"),
        ["run.cancelled"] = (ConsoleColor.Gray, "🛑"),
        ["approval.request"] = (ConsoleColor.DarkYellow, "⏸️"),
        ["approval.responded"] = (ConsoleColor.DarkGreen, "▶️"),
    };

    static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        var runClient = host.Services.GetRequiredService<IHermesRunClient>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        while (true)
        {
            Console.Clear();
            Console.WriteLine("🤖 Hermes Agent Runs API 演示");
            Console.WriteLine("══════════════════════════════════════");
            Console.WriteLine("  1. 实时事件流 + 自动审批    (RunWithLoggingAsync)");
            Console.WriteLine("  2. 轮询等待结果              (GetRunStatusAsync)");
            Console.WriteLine("  3. 中断正在执行的 Run        (StopRunAsync)");
            Console.WriteLine("  4. 交互式手动审批            (ApproveRunAsync)");
            Console.WriteLine("  5. 批量并发启动              (StartAsync)");
            Console.WriteLine("  6. 退出");
            Console.WriteLine("══════════════════════════════════════");
            Console.Write("请选择 [1-6]: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await Demo1_EventStream(runClient);
                    break;
                case "2":
                    await Demo2_Polling(runClient);
                    break;
                case "3":
                    await Demo3_Stop(runClient);
                    break;
                case "4":
                    await Demo4_Approval(runClient);
                    break;
                case "5":
                    await Demo5_BatchStart(runClient);
                    break;
                case "6":
                    return;
                default:
                    Console.WriteLine("无效选择，按任意键继续...");
                    Console.ReadKey(true);
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════
    //  公共辅助方法
    // ═══════════════════════════════════════════

    static string ReadPrompt(string modeName)
    {
        Console.Clear();
        Console.WriteLine($"📌 {modeName}");
        Console.WriteLine($"默认: \"{DefaultPrompt}\"");
        Console.Write("输入 prompt (直接回车使用默认): ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? DefaultPrompt : input;
    }

    /// <summary>打印彩色事件行（供 demo 1/3/4 共用）。</summary>
    static void PrintEvent(RunEvent evt)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var (color, icon) = EventStyles.TryGetValue(evt.Type, out var s) ? s : (ConsoleColor.DarkGray, "❓");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{timestamp}  ");
        Console.ForegroundColor = color;
        Console.WriteLine($"{icon} {evt.Type,-20}");
        Console.ResetColor();
    }

    static string Truncate(string text, int maxLength) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength
            ? text ?? ""
            : text[..maxLength] + "...";

    // ═══════════════════════════════════════════
    //  Demo 1: 实时事件流 + 自动审批
    //  展示: RunWithLoggingAsync + eventaction 回调 + ApprovalRequest.SetSessions()
    // ═══════════════════════════════════════════

    static async Task Demo1_EventStream(IHermesRunClient runClient)
    {
        var prompt = ReadPrompt("实时事件流 + 自动审批");
        Console.Clear();
        Console.WriteLine($"📡 订阅事件流: {Truncate(prompt, 60)}");
        Console.WriteLine(new string('─', 60));

        try
        {
            // RunWithLoggingAsync 封装了 Start → SubscribeEvents → 等待完成的全流程
            // eventaction 回调在每个事件到达时触发，可在此做审批 / 打印等
            await runClient.RunWithLoggingAsync(prompt, eventaction: (@event, runid) =>
            {
                // 自动批准审批请求（适合非交互场景）
                if (@event.IsApproval())
                {
                    runClient.ApproveRunAsync(runid, ApprovalRequest.Instance.SetSessions());
                }

                PrintEvent(@event);
            });
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ 执行失败: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine(new string('─', 60));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ 事件流结束");
        Console.ResetColor();
        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    // ═══════════════════════════════════════════
    //  Demo 2: 轮询等待结果
    //  展示: StartAsync → GetRunStatusAsync 轮询 → 状态迁移可视化
    // ═══════════════════════════════════════════

    static async Task Demo2_Polling(IHermesRunClient runClient)
    {
        var prompt = ReadPrompt("轮询等待结果");
        Console.Clear();

        // 启动 Run
        var startResp = await runClient.StartAsync(prompt);
        var runId = startResp.RunId;
        Console.WriteLine($"🚀 已启动: {runId}");
        Console.WriteLine(new string('─', 60));

        var lastStatus = "";
        var attempt = 0;

        // 轮询直到终态
        while (true)
        {
            attempt++;
            var status = await runClient.GetRunStatusAsync(runId);

            if (status is null)
            {
                Console.WriteLine("⚠️  Run 已过期（超过 1h TTL）");
                break;
            }

            // 状态变化时打印
            if (status.Status != lastStatus)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  [{attempt:D2}] {lastStatus} → {status.Status}");
                Console.ResetColor();
                lastStatus = status.Status;
            }

            // 终态判断
            if (status.Status is "completed" or "failed" or "cancelled")
            {
                Console.WriteLine(new string('─', 60));

                if (status.Status == "completed")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ 完成");
                    if (!string.IsNullOrEmpty(status.Output))
                        Console.WriteLine($"   Output: {Truncate(status.Output, 200)}");
                    if (status.Usage is not null)
                        Console.WriteLine($"   Tokens: {status.Usage.PromptTokens}→{status.Usage.CompletionTokens} (total {status.Usage.TotalTokens})");
                }
                else if (status.Status == "failed")
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ 失败: {status.Error ?? "未知错误"}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"🛑 已取消");
                }
                Console.ResetColor();
                break;
            }

            // 非终态显示进度
            Console.Write($"  [{attempt:D2}] {status.Status} {status.LastEvent ?? ""}");

            await Task.Delay(1500);
        }

        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    // ═══════════════════════════════════════════
    //  Demo 3: 中断正在执行的 Run
    //  展示: StartAsync → SubscribeEventsAsync → 用户按键 → StopRunAsync → 轮询确认取消
    // ═══════════════════════════════════════════

    static async Task Demo3_Stop(IHermesRunClient runClient)
    {
        var prompt = ReadPrompt("中断 Run");
        Console.Clear();

        // 启动 Run
        var startResp = await runClient.StartAsync(prompt);
        var runId = startResp.RunId;
        Console.WriteLine($"🚀 已启动: {runId}");
        Console.WriteLine("📡 实时事件流中...（按 Enter 中断）");
        Console.WriteLine(new string('─', 60));

        using var cts = new CancellationTokenSource();

        // 后台监听按键
        var keyTask = Task.Run(() =>
        {
            Console.ReadLine();
            cts.Cancel();
        });

        try
        {
            // 订阅事件流，实时打印
            await foreach (var evt in runClient.SubscribeEventsAsync(runId, cts.Token))
            {
                PrintEvent(evt);

                if (evt.Type == "run.completed" || evt.Type == "run.failed")
                {
                    Console.WriteLine(new string('─', 60));
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✅ Run 已完成（无需中断）");
                    Console.ResetColor();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户按下 Enter → 中断
            Console.WriteLine(new string('─', 60));
            Console.Write("🛑 正在发送中断请求... ");

            var stopResp = await runClient.StopRunAsync(runId);
            Console.WriteLine($"status: {stopResp.Status}");
            Console.WriteLine("⏳ 等待确认取消...");

            // 轮询确认已取消
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                var status = await runClient.GetRunStatusAsync(runId);
                if (status is null || status.Status == "cancelled")
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("🛑 Run 已取消");
                    Console.ResetColor();
                    break;
                }

                Console.Write(".");
            }
        }

        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    // ═══════════════════════════════════════════
    //  Demo 4: 交互式手动审批
    //  展示: StartAsync → SubscribeEvents → 用户选择 → ApproveRunAsync
    // ═══════════════════════════════════════════

    static async Task Demo4_Approval(IHermesRunClient runClient)
    {
        // 提示用户输入可能触发审批的操作
        Console.Clear();
        Console.WriteLine("📌 交互式手动审批");
        Console.WriteLine("💡 提示: 输入可能触发审批的 prompt（如包含 shell/ping 命令的操作）");
        Console.WriteLine("   默认: 启动一个需要审批的工具操作");
        Console.Write("\n输入 prompt: ");
        var input = Console.ReadLine();
        var prompt = string.IsNullOrWhiteSpace(input) ? "帮我 ping 一下 baidu.com" : input;

        Console.Clear();

        // 启动 Run
        var startResp = await runClient.StartAsync(prompt);
        var runId = startResp.RunId;
        Console.WriteLine($"🚀 已启动: {runId}");
        Console.WriteLine(new string('─', 60));

        var completed = false;

        await foreach (var evt in runClient.SubscribeEventsAsync(runId))
        {
            PrintEvent(evt);

            // 遇到审批请求 → 交互式选择
            if (evt.IsApproval())
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(new string('─', 40));
                Console.WriteLine("⚠️  需要审批工具调用");
                Console.WriteLine("   [1] once    — 仅本次批准");
                Console.WriteLine("   [2] session — 本次会话批准");
                Console.WriteLine("   [3] deny    — 拒绝");
                Console.Write("请选择 [1-3]: ");
                Console.ResetColor();

                var choice = Console.ReadLine();

                var approval = choice switch
                {
                    "1" => ApprovalRequest.Instance with { Choice = "once" },
                    "2" => ApprovalRequest.Instance.SetSessions(),
                    "3" => ApprovalRequest.Instance.SetDeny(),
                    _ => ApprovalRequest.Instance.SetSessions()
                };

                var resp = await runClient.ApproveRunAsync(runId, approval);
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"▶️  审批已处理: choice={resp.Choice}, resolved={resp.Resolved}");
                Console.ResetColor();
                Console.WriteLine(new string('─', 40));
            }

            // 完成/失败信号
            if (evt.Type == "run.completed")
            {
                completed = true;
                Console.WriteLine(new string('─', 60));
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Run 完成");
                Console.ResetColor();
                break;
            }

            if (evt.Type == "run.failed")
            {
                Console.WriteLine(new string('─', 60));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Run 失败");
                Console.ResetColor();
                break;
            }
        }

        if (!completed)
            Console.WriteLine("\n⚠️  事件流结束（可能未触发审批或 Run 已超时）");

        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    // ═══════════════════════════════════════════
    //  Demo 5: 批量并发启动
    //  展示: StartAsync × N → 收集 RunStartResponse → 表格展示
    // ═══════════════════════════════════════════

    static async Task Demo5_BatchStart(IHermesRunClient runClient)
    {
        Console.Clear();
        Console.WriteLine("🚀 批量启动模式");
        Console.WriteLine("输入多个 prompt（逗号分隔），将并发启动所有任务。");
        Console.WriteLine("默认: \"分析项目性能瓶颈, 审查代码安全性, 检查依赖版本兼容性\"");
        Console.Write("\n输入 prompts: ");
        var input = Console.ReadLine();

        var prompts = string.IsNullOrWhiteSpace(input)
            ? new[] { "分析项目性能瓶颈", "审查代码安全性", "检查依赖版本兼容性" }
            : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Console.Clear();
        Console.WriteLine($"🚀 并发启动 {prompts.Length} 个 Run...");
        Console.WriteLine(new string('─', 66));

        try
        {
            // 并发启动所有 Run
            var tasks = prompts.Select(async (p, i) =>
            {
                var resp = await runClient.StartAsync(p);
                return (Index: i + 1, Prompt: p, resp.RunId, resp.Status);
            });

            var results = await Task.WhenAll(tasks);

            // 表格展示结果
            Console.WriteLine($" {"#",-3} {"Run ID",-26} {"Status",-10} Prompt");
            Console.WriteLine(new string('─', 66));

            foreach (var r in results)
            {
                Console.WriteLine($" {r.Index,-3} {r.RunId,-26} {r.Status,-10} {Truncate(r.Prompt, 40)}");
            }

            Console.WriteLine(new string('─', 66));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ 全部 {results.Length} 个 Run 已启动");
            Console.ResetColor();
            Console.WriteLine($"\n💡 提示: 使用 GetRunStatusAsync 可轮询这些 Run 的状态。");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ 批量启动失败: {ex.Message}");
            Console.ResetColor();
        }

        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    // ═══════════════════════════════════════════
    //  应用配置
    // ═══════════════════════════════════════════

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHermesAgent(context.Configuration);
            });
}

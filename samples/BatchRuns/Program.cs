using HermesAgent.Sdk;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// BatchRuns 示例程序
/// 演示并发启动多个 Run 任务，通过 ANSI 动态仪表盘实时监控进度。
/// 当控制台被重定向时（CI/CD 环境），自动降级为逐行输出。
/// </summary>
class Program
{
    // ──────────────────────────────────────────
    // 硬编码演示 prompts（任务 9.2）
    // ──────────────────────────────────────────
    private static readonly string[] DemoPrompts = new[]
    {
        "分析 HermesAgent.Sdk 项目中可能存在的性能瓶颈",
        "审查 src/Clients/HermesRunClient.cs 的代码安全性",
        "检查项目依赖包的版本兼容性问题",
        "总结 HermesAgent.Sdk 的架构设计亮点",
        "查找 .csproj 文件中配置可能存在的问题",
    };

    // ──────────────────────────────────────────
    // 每个任务的状态（任务 10.2）
    // ──────────────────────────────────────────
    private sealed record RunState
    {
        public string RunId { get; init; } = "";
        public string Prompt { get; init; } = "";
        public int Index { get; init; }
        public int EventsReceived { get; set; }
        public string Status { get; set; } = "pending";  // pending, running, completed, failed, cancelled
        public string LastEvent { get; set; } = "等待中...";
        public string? Output { get; set; }
        public string? ErrorMessage { get; set; }

        public int Progress
        {
            get
            {
                if (Status is "completed" or "failed" or "cancelled") return 100;
                var pct = EventsReceived * 20;
                return Math.Min(pct, 95);
            }
        }
    }

    // ──────────────────────────────────────────
    // 仪表盘布局常量
    // ──────────────────────────────────────────
    private const int DashboardTop = 1;
    private const int TaskStartLine = 4;

    // 线程安全锁（任务 11.3）
    private static readonly object ConsoleLock = new();

    static async Task Main(string[] args)
    {
        // ── 任务 9.1: 检测输出重定向 ──
        var isRedirected = Console.IsOutputRedirected;
        var host = CreateHostBuilder(args).Build();
        var runClient = host.Services.GetRequiredService<IHermesRunClient>();

        Console.WriteLine("🚀 BatchRuns — 并发批处理演示");
        Console.WriteLine("═══════════════════════════════");

        if (isRedirected)
        {
            Console.WriteLine("⚠️  检测到输出重定向，使用逐行输出模式。");
        }

        // ── 任务 10.1: 并发启动所有 Run ──
        var states = new ConcurrentDictionary<string, RunState>();
        var runIds = new List<string>();

        Console.WriteLine($"\n📦 准备启动 {DemoPrompts.Length} 个任务...");
        var sw = Stopwatch.StartNew();

        var startTasks = DemoPrompts.Select(async (prompt, i) =>
        {
            try
            {
                var runId = await runClient.StartAsync(prompt);
                var state = new RunState
                {
                    RunId = runId,
                    Prompt = prompt,
                    Index = i + 1,
                };
                states[runId] = state;
                return runId;
            }
            catch (Exception ex)
            {
                lock (ConsoleLock)
                {
                    Console.WriteLine($"❌ 启动任务 [{i + 1}] 失败: {ex.Message}");
                }
                return null;
            }
        });

        var results = await Task.WhenAll(startTasks);
        runIds = results.Where(id => id != null).Cast<string>().ToList();

        if (runIds.Count == 0)
        {
            Console.WriteLine("❌ 没有任务成功启动，退出。");
            return;
        }

        Console.WriteLine($"✅ 已启动 {runIds.Count}/{DemoPrompts.Length} 个任务\n");

        // ── 根据重定向选择输出模式 ──
        if (isRedirected)
        {
            // 任务 12.3: 降级模式
            await RunFallbackMode(runClient, states, runIds);
        }
        else
        {
            DrawDashboardFrame(states, runIds);
            // 任务 12.1: 并行 SSE 订阅
            await RunDashboardMode(runClient, states, runIds);
        }

        sw.Stop();

        // ── 任务 13.1-13.2: 结果汇总 ──
        ShowSummary(states, runIds, sw.Elapsed);

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey(true);
    }

    // ══════════════════════════════════════════════
    // 仪表盘模式（ANSI 动态刷新）
    // ══════════════════════════════════════════════

    static void DrawDashboardFrame(ConcurrentDictionary<string, RunState> states, List<string> runIds)
    {
        lock (ConsoleLock)
        {
            Console.Clear();
            Console.WriteLine("┌──────────────────────────────────────────────────────┐");
            Console.WriteLine("│  Batch Runs 进度仪表盘                                │");
            Console.WriteLine("├──────────────────────────────────────────────────────┤");

            // 预留空行给任务进度
            var sorted = runIds.Select(id => states.GetValueOrDefault(id)).Where(s => s != null).OrderBy(s => s!.Index);
            foreach (var state in sorted)
            {
                Console.WriteLine("│                                                    │");
            }

            Console.WriteLine("├──────────────────────────────────────────────────────┤");
            Console.WriteLine("│  等待开始...                                         │");
            Console.WriteLine("└──────────────────────────────────────────────────────┘");
        }
    }

    static void UpdateDashboard(ConcurrentDictionary<string, RunState> states, List<string> runIds)
    {
        lock (ConsoleLock)
        {
            int originalTop = Console.CursorTop;
            int originalLeft = Console.CursorLeft;

            var sorted = runIds.Select(id => states.GetValueOrDefault(id))
                               .Where(s => s != null)
                               .OrderBy(s => s!.Index)
                               .ToList();

            // 绘制每行任务进度
            for (int i = 0; i < sorted.Count; i++)
            {
                var s = sorted[i]!;
                Console.SetCursorPosition(0, TaskStartLine + i);
                DrawTaskLine(s);
            }

            // 绘制底部状态栏
            int summaryLine = TaskStartLine + sorted.Count;
            Console.SetCursorPosition(0, summaryLine);
            Console.Write("├──────────────────────────────────────────────────────┤");

            int completed = sorted.Count(s => s!.Status is "completed");
            int failed = sorted.Count(s => s!.Status is "failed");
            int cancelled = sorted.Count(s => s!.Status is "cancelled");
            int running = sorted.Count(s => s!.Status is "running" or "pending");

            Console.SetCursorPosition(0, summaryLine + 1);
            var summary = $"│  ✅ {completed} 完成 | ❌ {failed} 失败 | ⊘ {cancelled} 取消 | 🔄 {running} 运行中";
            Console.Write(summary.PadRight(54) + "│");

            Console.SetCursorPosition(0, summaryLine + 2);
            Console.Write("└──────────────────────────────────────────────────────┘");

            // 恢复光标
            Console.SetCursorPosition(originalLeft, originalTop);
        }
    }

    static void DrawTaskLine(RunState s)
    {
        // 任务 11.1: 进度条渲染
        var bar = RenderProgressBar(s.Progress);
        var icon = s.Status switch
        {
            "completed" => "✅",
            "failed" => "❌",
            "cancelled" => "⊘",
            _ => "  ",
        };

        var statusText = s.Status switch
        {
            "completed" => "完成",
            "failed" => "失败",
            "cancelled" => "取消",
            "running" => "运行中",
            "pending" => "等待中",
            _ => s.Status,
        };

        var line = $"│ {s.Index}. {bar} {s.Progress,3}% {icon} {statusText,-4} {Truncate(s.LastEvent, 24)}";
        Console.Write(line.PadRight(54) + "│");
    }

    // 任务 11.1: 进度条辅助函数
    static string RenderProgressBar(int percent)
    {
        int filled = percent / 10;
        int empty = 10 - filled;
        return new string('█', filled) + new string('░', empty);
    }

    static async Task RunDashboardMode(
        IHermesRunClient runClient,
        ConcurrentDictionary<string, RunState> states,
        List<string> runIds)
    {
        // 任务 12.1: 为每个 runId 启动并行 SSE 订阅
        var subscribeTasks = runIds.Select(runId => MonitorRun(runClient, states, runId, runIds));
        await Task.WhenAll(subscribeTasks);
    }

    static async Task MonitorRun(
        IHermesRunClient runClient,
        ConcurrentDictionary<string, RunState> states,
        string runId,
        List<string> runIds)
    {
        if (!states.TryGetValue(runId, out var state)) return;

        state.Status = "running";
        state.LastEvent = "已启动";
        UpdateDashboard(states, runIds);

        try
        {
            await foreach (var evt in runClient.SubscribeEventsAsync(runId))
            {
                state.EventsReceived++;

                switch (evt.Type)
                {
                    case "tool_started":
                        var toolName = evt.Data?.GetValueOrDefault("tool_name")?.ToString();
                        state.LastEvent = toolName ?? "工具执行";
                        break;
                    case "tool_completed":
                        state.LastEvent = $"工具完成";
                        break;
                    case "reasoning":
                        state.LastEvent = "推理中...";
                        break;
                    case "completion":
                        state.Status = "completed";
                        state.LastEvent = "✅ 完成";
                        if (evt.Data?.TryGetValue("content", out var content) == true)
                            state.Output = content?.ToString();
                        UpdateDashboard(states, runIds);
                        return;
                    case "error":
                        state.Status = "failed";
                        state.LastEvent = "❌ 错误";
                        if (evt.Data?.TryGetValue("message", out var msg) == true)
                            state.ErrorMessage = msg?.ToString();
                        UpdateDashboard(states, runIds);
                        return;
                    case "cancelled":
                        state.Status = "cancelled";
                        state.LastEvent = "已取消";
                        UpdateDashboard(states, runIds);
                        return;
                }

                // 任务 12.2: 进度估算 — terminal 事件已在上方处理，这里是中间事件
                UpdateDashboard(states, runIds);
            }

            // 流正常结束但未收到 terminal 事件
            state.Status = "completed";
            state.LastEvent = "流结束";
            UpdateDashboard(states, runIds);
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            state.LastEvent = "异常";
            state.ErrorMessage = ex.Message;
            UpdateDashboard(states, runIds);
        }
    }

    // ══════════════════════════════════════════════
    // 降级模式（控制台重定向）（任务 12.3）
    // ══════════════════════════════════════════════

    static async Task RunFallbackMode(
        IHermesRunClient runClient,
        ConcurrentDictionary<string, RunState> states,
        List<string> runIds)
    {
        var subscribeTasks = runIds.Select(runId => MonitorRunFallback(runClient, states, runId));
        await Task.WhenAll(subscribeTasks);
    }

    static async Task MonitorRunFallback(
        IHermesRunClient runClient,
        ConcurrentDictionary<string, RunState> states,
        string runId)
    {
        if (!states.TryGetValue(runId, out var state)) return;

        state.Status = "running";
        Console.WriteLine($"[{state.Index}] {runId} 开始: {Truncate(state.Prompt, 50)}");

        try
        {
            await foreach (var evt in runClient.SubscribeEventsAsync(runId))
            {
                var label = evt.Type switch
                {
                    "tool_started" => $"🔧 {evt.Data?.GetValueOrDefault("tool_name")}",
                    "tool_completed" => "✅ 工具完成",
                    "reasoning" => "💡 推理中",
                    "completion" => $"🎯 完成: {Truncate(evt.Data?.GetValueOrDefault("content")?.ToString(), 80)}",
                    "error" => $"❌ {evt.Data?.GetValueOrDefault("message")}",
                    "cancelled" => "🛑 已取消",
                    _ => evt.Type,
                };
                Console.WriteLine($"  [{state.Index}] {label}");

                if (evt.Type == "completion") { state.Status = "completed"; break; }
                if (evt.Type == "error") { state.Status = "failed"; break; }
                if (evt.Type == "cancelled") { state.Status = "cancelled"; break; }
            }
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            state.ErrorMessage = ex.Message;
            Console.WriteLine($"  [{state.Index}] ❌ 异常: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    // 结果汇总（任务 13.1-13.2）
    // ══════════════════════════════════════════════

    static void ShowSummary(
        ConcurrentDictionary<string, RunState> states,
        List<string> runIds,
        TimeSpan elapsed)
    {
        if (!Console.IsOutputRedirected)
        {
            // 清空屏幕显示汇总
            lock (ConsoleLock)
            {
                Console.Clear();
            }
        }

        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║               Batch Runs 结果汇总                    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");

        var sorted = runIds.Select(id => states.GetValueOrDefault(id))
                           .Where(s => s != null)
                           .OrderBy(s => s!.Index);

        int completed = 0, failed = 0, cancelled = 0, running = 0;
        foreach (var s in sorted)
        {
            var icon = s!.Status switch
            {
                "completed" => "✅",
                "failed" => "❌",
                "cancelled" => "⊘",
                _ => "🔄",
            };

            Console.WriteLine($"║ {icon} [{s.Index}] {s.RunId,-14} {s.Status,-10} {Truncate(s.Prompt, 28)}");

            switch (s.Status)
            {
                case "completed": completed++; break;
                case "failed": failed++; break;
                case "cancelled": cancelled++; break;
                default: running++; break;
            }
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  ✅ 成功: {completed}  |  ❌ 失败: {failed}  |  ⊘ 取消: {cancelled}  |  🔄 未完成: {running}");
        Console.WriteLine($"║  ⏱  总耗时: {elapsed.TotalSeconds:F1}s");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");

        // 任务 13.2: 显示失败任务的错误详情
        var failedStates = sorted.Where(s => s!.Status == "failed").ToList();
        if (failedStates.Count > 0)
        {
            Console.WriteLine("\n📋 失败详情:");
            foreach (var s in failedStates)
            {
                Console.WriteLine($"  [{s!.Index}] {Truncate(s.ErrorMessage ?? "未知错误", 80)}");
            }
        }
    }

    // ══════════════════════════════════════════════
    // 辅助函数
    // ══════════════════════════════════════════════

    static string Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) || text!.Length <= maxLength
            ? text ?? ""
            : text[..maxLength] + "...";

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHermesAgent(context.Configuration);
            });
}

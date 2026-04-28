using HermesAgent.Sdk;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// ConsoleRuns 示例程序
/// 演示 HermesAgent.Sdk Runs API 的四种核心使用模式：
///   SSE 实时事件流、RunAndWait 阻塞结果、RunWithLogging 日志输出、批量启动
/// </summary>
class Program
{
    private const string DefaultPrompt = "分析 HermesAgent.Sdk 项目中可能存在的性能瓶颈";

    private static readonly Dictionary<string, (ConsoleColor Color, string Icon)> EventStyles = new()
    {
        ["run_started"] = (ConsoleColor.Cyan, "🚀"),
        ["tool_started"] = (ConsoleColor.Yellow, "🔧"),
        ["tool_completed"] = (ConsoleColor.Green, "✅"),
        ["reasoning"] = (ConsoleColor.Magenta, "💡"),
        ["completion"] = (ConsoleColor.White, "🎯"),
        ["error"] = (ConsoleColor.Red, "❌"),
        ["cancelled"] = (ConsoleColor.Gray, "🛑"),
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
            Console.WriteLine("════════════════════════════════");
            Console.WriteLine("  1. SSE 实时事件流");
            Console.WriteLine("  2. RunAndWait 阻塞等待结果");
            Console.WriteLine("  3. RunWithLogging 结构化日志");
            Console.WriteLine("  4. 批量启动 (收集 runId)");
            Console.WriteLine("  5. 退出");
            Console.WriteLine("════════════════════════════════");
            Console.Write("请选择 [1-5]: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await DemonstrateSseEventStream(runClient);
                    break;
                case "2":
                    await DemonstrateRunAndWait(runClient);
                    break;
                case "3":
                    await DemonstrateRunWithLogging(runClient, logger);
                    break;
                case "4":
                    await DemonstrateBatchStart(runClient);
                    break;
                case "5":
                    return;
                default:
                    Console.WriteLine("无效选择，按任意键继续...");
                    Console.ReadKey(true);
                    break;
            }
        }
    }

    static string ReadPrompt(string modeName)
    {
        Console.Clear();
        Console.WriteLine($"📌 {modeName}");
        Console.WriteLine($"默认: \"{DefaultPrompt}\"");
        Console.Write("输入 prompt (直接回车使用默认): ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? DefaultPrompt : input;
    }

    /// <summary>
    /// 模式 1: SSE 实时事件流 — 彩色事件类型 + emoji 图标
    /// </summary>
    static async Task DemonstrateSseEventStream(IHermesRunClient runClient)
    {
        var prompt = ReadPrompt("SSE 实时事件流");
        Console.Clear();
        Console.WriteLine($"📡 订阅事件流: {prompt}");
        Console.WriteLine(new string('─', 60));

        try
        {
            var runId = await runClient.StartAsync(prompt);

            await foreach (var evt in runClient.SubscribeEventsAsync(runId))
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var (color, icon) = EventStyles.TryGetValue(evt.Type, out var s) ? s : (ConsoleColor.DarkGray, "❓");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{timestamp}  ");
                Console.ForegroundColor = color;
                Console.Write($"{icon} {evt.Type,-16}");

                Console.ResetColor();
                PrintEventDetail(evt);
            }
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

    static void PrintEventDetail(RunEvent evt)
    {
        switch (evt.Type)
        {
            case "run_started":
                var model = evt.Data?.GetValueOrDefault("model")?.ToString() ?? "?";
                Console.WriteLine($"模型: {model}");
                break;
            case "tool_started":
                var toolName = evt.Data?.GetValueOrDefault("tool_name")?.ToString() ?? "?";
                Console.WriteLine(toolName);
                break;
            case "tool_completed":
                var compName = evt.Data?.GetValueOrDefault("tool_name")?.ToString() ?? "?";
                var duration = evt.Data?.GetValueOrDefault("duration_ms")?.ToString() ?? "?";
                Console.WriteLine($"{compName} (耗时 {duration}ms)");
                break;
            case "reasoning":
                var reasoning = evt.Data?.GetValueOrDefault("content")?.ToString() ?? "";
                Console.WriteLine(Truncate(reasoning, 100));
                break;
            case "completion":
                var content = evt.Data?.GetValueOrDefault("content")?.ToString() ?? "";
                Console.WriteLine(content);
                break;
            case "error":
                var msg = evt.Data?.GetValueOrDefault("message")?.ToString() ?? "未知错误";
                Console.WriteLine(msg);
                break;
            case "cancelled":
                var reason = evt.Data?.GetValueOrDefault("reason")?.ToString() ?? "";
                Console.WriteLine(reason);
                break;
            default:
                Console.WriteLine(evt.Data);
                break;
        }
    }

    /// <summary>
    /// 模式 2: RunAndWait — 阻塞等待 + 旋转动画 + 结果展示
    /// </summary>
    static async Task DemonstrateRunAndWait(IHermesRunClient runClient)
    {
        var prompt = ReadPrompt("RunAndWait 阻塞等待结果");
        Console.Clear();
        Console.WriteLine($"⏳ 正在运行: \"{prompt}\"");

        var animationChars = new[] { '|', '/', '─', '\\' };
        var animIndex = 0;
        using var cts = new CancellationTokenSource();
        var animationTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    Console.Write($"\r⏳ 等待中 {animationChars[animIndex]}  ");
                    animIndex = (animIndex + 1) % animationChars.Length;
                    await Task.Delay(200, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            var result = await runClient.RunAndWaitAsync(prompt);
            cts.Cancel();
            await Task.WhenAny(animationTask, Task.Delay(100));

            Console.Write("\r                    \r"); // 清除动画行
            Console.WriteLine("✅ 运行完成！");
            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"状态: {result.Status}");
            Console.WriteLine($"耗时: {(result.DurationMs.HasValue ? $"{result.DurationMs}ms" : "N/A")}");
            Console.WriteLine($"工具调用: {(result.ToolCallCount.HasValue ? $"{result.ToolCallCount} 次" : "N/A")}");
            Console.WriteLine(new string('─', 50));

            if (!string.IsNullOrEmpty(result.Output))
            {
                Console.WriteLine("输出:");
                Console.WriteLine(result.Output);
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"错误: {result.ErrorMessage}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            cts.Cancel();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ 执行失败: {ex.Message}");
            Console.ResetColor();
        }

        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    /// <summary>
    /// 模式 3: RunWithLogging — 通过 ILogger 输出结构化日志
    /// </summary>
    static async Task DemonstrateRunWithLogging(IHermesRunClient runClient, ILogger<Program> logger)
    {
        var prompt = ReadPrompt("RunWithLogging 结构化日志");
        Console.Clear();
        Console.WriteLine($"📋 RunWithLogging 模式");
        Console.WriteLine($"Prompt: \"{prompt}\"");
        Console.WriteLine(new string('─', 60));

        try
        {
            await runClient.RunWithLoggingAsync(prompt, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RunWithLogging 执行失败");
        }

        Console.WriteLine(new string('─', 60));
        Console.Write("\n按任意键返回主菜单...");
        Console.ReadKey(true);
    }

    /// <summary>
    /// 模式 4: 批量启动 — 逗号分隔多 prompt → 并发 StartAsync → 收集 runId
    /// </summary>
    static async Task DemonstrateBatchStart(IHermesRunClient runClient)
    {
        Console.Clear();
        Console.WriteLine("🚀 批量启动模式");
        Console.WriteLine("输入多个 prompt（逗号分隔），将并发启动所有任务。");
        Console.WriteLine($"默认: \"分析性能瓶颈, 审查代码安全性, 检查依赖版本\"");
        Console.Write("输入 prompts: ");
        var input = Console.ReadLine();

        var prompts = string.IsNullOrWhiteSpace(input)
            ? new[] { "分析项目性能瓶颈", "审查代码安全性", "检查依赖版本兼容性" }
            : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Console.Clear();
        Console.WriteLine($"🚀 批量启动 {prompts.Length} 个任务...");
        Console.WriteLine(new string('─', 60));

        try
        {
            var tasks = prompts.Select(async (p, i) =>
            {
                var runId = await runClient.StartAsync(p);
                Console.WriteLine($"  [{i + 1}] runId: {runId} ← {Truncate(p, 50)}");
                return (Index: i + 1, Prompt: p, RunId: runId);
            });

            var results = await Task.WhenAll(tasks);

            Console.WriteLine(new string('─', 60));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ 全部 {results.Length} 个任务已启动！");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("收集到的 runId 列表:");
            foreach (var r in results)
            {
                Console.WriteLine($"  [{r.Index}] {r.RunId} — {Truncate(r.Prompt, 40)}");
            }
            Console.WriteLine();
            Console.WriteLine("💡 提示: 使用 BatchRuns 示例项目可监控这些任务的实时进度。");
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

    static string Truncate(string text, int maxLength) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength
            ? text ?? ""
            : text[..maxLength] + "...";

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

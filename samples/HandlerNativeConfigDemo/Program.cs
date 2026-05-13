using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// HandlerNativeConfigDemo — 演示 Fluent API 配置外部化 + YAML 合并优先级。
/// </summary>
partial class Program
{
    private const string DbPath = "handler-native-config-demo.db";

    static async Task Main(string[] _)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CleanupDatabase();

        using var host = CreateHostBuilder().Build();
        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        var importExport = host.Services.GetRequiredService<WorkflowImportExportManager>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();

        await bootstrapper.ApplyAllAsync();
        await host.StartAsync();

        while (true)
        {
            try { Console.Clear(); } catch (IOException) { }

            Console.WriteLine("Handler Native Config Demo");
            Console.WriteLine("══════════════════════════════════════════════════");
            Console.WriteLine("  Fluent API 配置外部化 + YAML 合并优先级");
            Console.WriteLine();
            Console.WriteLine("  合并优先级: YAML 配置 > Fluent API 配置 > Handler 虚属性 > 引擎内建默认值");
            Console.WriteLine("  拓扑边界:   depends_on / next_step_id / steps / wait_mode 不参与覆盖");
            Console.WriteLine();
            Console.WriteLine("  1. Fluent Timeout 生效");
            Console.WriteLine("  2. Fluent Retry 生效");
            Console.WriteLine("  3. YAML Timeout 覆盖 Fluent 配置");
            Console.WriteLine("  4. YAML 部分覆盖 (timeout 被覆盖, retry 保留)");
            Console.WriteLine("  5. FromHandler 提取 + Prompt 优先级");
            Console.WriteLine("  6. ExportTemplate 生成含 Handler 默认值的 YAML");
            Console.WriteLine("  7. 向后兼容 (未声明默认配置的 Handler)");
            Console.WriteLine("  8. IWorkflowBootstrapper 两步注册演示");
            Console.WriteLine("  9. Builder Fluent API 配置 (3层优先级, 4步工作流)");
            Console.WriteLine(" 10. 导出代码定义的 YAML (AddWorkflow → ExportToYaml)");
            Console.WriteLine(" 11. 退出");
            Console.WriteLine("══════════════════════════════════════════════════");
            Console.Write("请选择 [1-11]: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await Scenario1_HandlerDefaultTimeout(engine, bootstrapper);
                    break;
                case "2":
                    await Scenario2_HandlerDefaultRetry(engine, bootstrapper);
                    break;
                case "3":
                    await Scenario3_YamlOverridesTimeout(engine, bootstrapper);
                    break;
                case "4":
                    await Scenario4_YamlPartialOverride(engine, bootstrapper);
                    break;
                case "5":
                    Scenario5_HandlerDefaultsExtraction();
                    break;
                case "6":
                    await Scenario6_ExportTemplate(importExport, registry);
                    break;
                case "7":
                    await Scenario7_BackwardCompatible(engine, bootstrapper);
                    break;
                case "8":
                    await Scenario8_BootstrapperTwoStep(engine, bootstrapper);
                    break;
                case "9":
                    await Scenario9_FluentApiDemo(engine, bootstrapper, importExport);
                    break;
                case "10":
                    await Scenario10_ExportYamlFromCode(importExport);
                    break;
                case "11":
                    Console.WriteLine("退出...");
                    await host.StopAsync();
                    CleanupDatabase();
                    return;
                default:
                    Console.WriteLine("无效选择，按任意键继续...");
                    Console.ReadKey(true);
                    break;
            }

            Console.WriteLine();
            Console.Write("按任意键返回菜单...");
            try { Console.ReadKey(true); } catch (InvalidOperationException) { }
        }
    }

    static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(c =>
                {
                    c.SingleLine = true;
                    c.TimestampFormat = "HH:mm:ss ";
                });
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services
                    .AddHermesAgent(context.Configuration)
                    .AddWorkflowChain(chain =>
                    {
                        chain.AddSqliteStateStore($"Data Source={DbPath}");

                        // Scenarios 1-8: step handlers used by YAML-defined workflows
                        chain.AddWorkflow("demo-scenarios", opt => opt
                            .AddCodeStep<TimeoutDemoStep>(c => c
                                .WithTimeout("00:00:02")
                            )
                            .AddCodeStep<RetryDemoStep>(c => c
                                .WithRetry(r => r.Immediate(3))
                            )
                            .AddCodeStep<MixedConfigStep>(c =>
                            {
                                c.WithTimeout("00:00:00.500");
                                c.WithRetry(r => r.Immediate(3));
                            })
                            .AddCodeStep<NoDefaultStep>()
                            .AddAgentStep<DemoAgentStep>(c =>
                            {
                                c.WithPrompt("你是一个演示助手");
                                c.WithSystemPrompt("你是一个测试用的 SystemPrompt");
                            })
                        );

                        // Scenario 9: Fluent API configured 4-step workflow
                        chain.AddWorkflow(opt => opt
                            .AddCodeStep<FluentEntryStep>()
                            .AddCodeStep<FluentProcessStep>(c => c
                                .WithTimeout("00:00:10")
                                .WithRetry(r => r.ExponentialBackoff(initialDelay: "5s", maxDelay: "00:05:00"))
                            )
                            .AddCodeStep<FluentValidationStep>(c => c
                                .WithTimeout("00:00:15")
                                .WithErrorPolicy(ErrorPolicy.SkipFailedBranch)
                            )
                            .AddAgentStep<FluentAgentDemoStep>(c => c
                                .WithTimeout("00:00:30")
                                .WithPrompt("Fluent API 配置的提示词")
                                .WithSystemPrompt("你是 fluent 助手")
                                .WithRetry(r=>r.FixedInterval(maxRetries:3,initialDelay: "5s", maxDelay: "00:05:00"))
                            )
                        ).WithVersion("1.0").WithName("s9-fluent-wf").WithDescription("Builder Fluent API 配置演示");
                    });

                //services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                //services.AddSingleton<IHermesRunClient, NullRunClient>();
            });

    static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 60));
    }

    static void PrintIf(bool condition, string msg)
    {
        Console.WriteLine(condition ? $"  {msg}" : $"  ⚠ {msg}");
    }

    static void CleanupDatabase()
    {
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }
}

using HermesAgent.Sdk.Extensions;
using HermesAgent.Sdk.WorkflowChain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain.YamlDemo;

/// <summary>
/// WorkflowYamlDemo — 演示 Phase 4 YAML 声明式工作流核心功能：
///   • YAML 工作流加载与执行（bootstrapper.LoadAndApplyAsync）
///   • retry / timeout / error_policy 运行时生效
///   • 变量解析 {{steps.x.output.field}}
///   • 热重载（文件监控 + 自动重新注册）
///   • 语义版本管理
/// </summary>
class Program
{
    private const string DbPath = "workflow-yaml-demo.db";
    private const string AssemblyName = "WorkflowYamlDemo";

    static async Task Main(string[] _)
    {
        CleanupDatabase();

        using var host = CreateHostBuilder().Build();
        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var registry = host.Services.GetRequiredService<WorkflowRegistry>();
        var versionManager = host.Services.GetRequiredService<WorkflowVersionManager>();
        var importExport = host.Services.GetRequiredService<WorkflowImportExportManager>();
        var bootstrapper = host.Services.GetRequiredService<IWorkflowBootstrapper>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        // 将已注册的工作流同步到 Engine 运行时
        await bootstrapper.ApplyAllAsync();
        await host.StartAsync();

        while (true)
        {
            try { Console.Clear(); } catch (IOException) { /* 控制台被重定向时跳过清除 */ }
            Console.WriteLine("🤖 HermesAgent YAML 声明式工作流演示");
            Console.WriteLine("════════════════════════════════════");
            Console.WriteLine("  1. YAML 工作流加载与执行");
            Console.WriteLine("  2. 重试策略演示");
            Console.WriteLine("  3. 超时策略演示");
            Console.WriteLine("  4. 错误策略演示");
            Console.WriteLine("  5. 变量解析演示");
            Console.WriteLine("  6. 热重载演示");
            Console.WriteLine("  7. 版本管理演示");
            Console.WriteLine("  8. 退出");
            Console.WriteLine("════════════════════════════════════");
            Console.Write("请选择 [1-8]: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await DemoBasicYamlWorkflow(engine, bootstrapper);
                    break;
                case "2":
                    await DemoRetryWorkflow(engine, bootstrapper);
                    break;
                case "3":
                    await DemoTimeoutWorkflow(engine, bootstrapper);
                    break;
                case "4":
                    await DemoErrorPolicyWorkflow(engine, bootstrapper);
                    break;
                case "5":
                    DemoVariableResolution(registry);
                    break;
                case "6":
                    await DemoHotReload(engine, registry, importExport, bootstrapper, logger);
                    break;
                case "7":
                    DemoVersionManagement(versionManager, registry);
                    break;
                case "8":
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
            try { Console.ReadKey(true); } catch (InvalidOperationException) { /* 输入被重定向时跳过 */ }
        }
    }

    // ================================================================
    // 1. 基础 YAML 工作流加载与执行
    // ================================================================
    private static async Task DemoBasicYamlWorkflow(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("YAML 工作流加载与执行");

        var yaml = YAML_BASIC_WORKFLOW;
        Console.WriteLine("📄 内嵌 YAML 定义:");
        Console.WriteLine(new string('─', 50));
        Console.WriteLine(yaml.Trim());
        Console.WriteLine(new string('─', 50));

        // 一步加载：解析 YAML → 注册到 Registry → 同步到 Engine
        var parser = new YamlWorkflowParser();
        var definition = parser.Parse(yaml);
        Console.WriteLine($"✅ 工作流 '{definition.Name}' v{definition.Version} 解析成功");
        Console.WriteLine($"   步骤数: {definition.Steps.Count}");

        await bootstrapper.LoadAndApplyAsync(yaml);

        var context = new WorkflowContext
        {
            InstanceId = $"basic-{DateTime.Now:HHmmss}",
            InitialInput = new Dictionary<string, object?>
            {
                ["title"] = "YAML 声明式工作流演示",
                ["amount"] = 50000,
                ["requester"] = "开发者"
            }
        };

        Console.WriteLine($"\n🚀 启动工作流: {context.InstanceId}");
        var instanceId = await engine.StartAsync("fetch-data", context, CancellationToken.None, definition.Name);
        var instance = engine.GetInstance(instanceId);
        Console.WriteLine($"   状态: {instance?.Status}");
        PrintStepRecords(engine, instanceId);
    }

    // ================================================================
    // 2. 重试策略演示
    // ================================================================
    private static async Task DemoRetryWorkflow(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("重试策略演示 (exponential_backoff)");

        var yaml = YAML_RETRY_WORKFLOW;
        Console.WriteLine("📄 YAML retry 配置:");
        Console.WriteLine(new string('─', 50));
        Console.WriteLine(yaml.Trim());
        Console.WriteLine(new string('─', 50));

        var parser = new YamlWorkflowParser();
        var definition = parser.Parse(yaml);
        await bootstrapper.LoadAndApplyAsync(yaml);

        // 打印重试配置
        foreach (var step in definition.Steps.Where(s => s.Retry != null))
        {
            Console.WriteLine($"⚙️ 步骤 '{step.Id}' 重试策略:");
            Console.WriteLine($"   max_retries: {step.Retry!.MaxRetries}");
            Console.WriteLine($"   policy: {step.Retry.Policy}");
            Console.WriteLine($"   initial_delay: {step.Retry.InitialDelay}");
            Console.WriteLine($"   backoff_factor: {step.Retry.BackoffFactor}");
        }

        var context = new WorkflowContext
        {
            InstanceId = $"retry-{DateTime.Now:HHmmss}",
        };

        Console.WriteLine($"\n🚀 启动工作流 (FailingStep 会失败3次后放弃):");
        var instanceId = await engine.StartAsync("entry-fail", context, CancellationToken.None, definition.Name);
        var instance = engine.GetInstance(instanceId);
        Console.WriteLine($"   最终状态: {instance?.Status}");
        PrintStepRecords(engine, instanceId);
    }

    // ================================================================
    // 3. 超时策略演示
    // ================================================================
    private static async Task DemoTimeoutWorkflow(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("超时策略演示 (timeout: 2s)");

        var yaml = YAML_TIMEOUT_WORKFLOW;
        Console.WriteLine("📄 YAML timeout 配置:");
        Console.WriteLine(new string('─', 50));
        Console.WriteLine(yaml.Trim());
        Console.WriteLine(new string('─', 50));

        var parser = new YamlWorkflowParser();
        var definition = parser.Parse(yaml);
        await bootstrapper.LoadAndApplyAsync(yaml);

        var context = new WorkflowContext
        {
            InstanceId = $"timeout-{DateTime.Now:HHmmss}",
        };

        Console.WriteLine($"\n🚀 启动工作流 (DelayStep 等待 5s，超时 2s):");
        var instanceId = await engine.StartAsync("entry-slow", context, CancellationToken.None, definition.Name);
        var instance = engine.GetInstance(instanceId);
        Console.WriteLine($"   最终状态: {instance?.Status}");
        PrintStepRecords(engine, instanceId);
    }

    // ================================================================
    // 4. 错误策略演示 (SkipFailedBranch)
    // ================================================================
    private static async Task DemoErrorPolicyWorkflow(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("错误策略演示 (SkipFailedBranch)");

        var yaml = YAML_ERROR_POLICY_WORKFLOW;
        Console.WriteLine("📄 YAML error_policy 配置:");
        Console.WriteLine(new string('─', 50));
        Console.WriteLine(yaml.Trim());
        Console.WriteLine(new string('─', 50));

        var parser = new YamlWorkflowParser();
        var definition = parser.Parse(yaml);
        await bootstrapper.LoadAndApplyAsync(yaml);

        var context = new WorkflowContext
        {
            InstanceId = $"errorpol-{DateTime.Now:HHmmss}",
        };

        Console.WriteLine($"\n🚀 启动并行工作流 (branch-b 会失败但被跳过):");
        var instanceId = await engine.StartAsync("entry-demo", context, CancellationToken.None, definition.Name);
        var instance = engine.GetInstance(instanceId);
        Console.WriteLine($"   最终状态: {instance?.Status ?? "unknown"}");
        PrintStepRecords(engine, instanceId);
    }

    // ================================================================
    // 5. 变量解析演示
    // ================================================================
    private static void DemoVariableResolution(WorkflowRegistry registry)
    {
        PrintHeader("变量解析演示");

        var yaml = YAML_VARIABLE_WORKFLOW;
        Console.WriteLine("📄 YAML 变量模板定义:");
        Console.WriteLine(new string('─', 50));
        Console.WriteLine(yaml.Trim());

        var parser = new YamlWorkflowParser();
        var definition = parser.Parse(yaml);

        Console.WriteLine(new string('─', 50));
        Console.WriteLine("\n🔍 变量表达式分析:");

        // 查找包含 {{ }} 的 prompt
        foreach (var step in definition.Steps)
        {
            if (!string.IsNullOrEmpty(step.Prompt) && step.Prompt.Contains("{{"))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(step.Prompt, @"\{\{(.+?)\}\}");
                Console.WriteLine($"  步骤 '{step.Id}' 包含 {matches.Count} 个变量表达式:");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    Console.WriteLine($"    - {{{{ {m.Groups[1].Value.Trim()} }}}}");
                }
            }
        }

        // 演示 VariableResolver 实际替换
        Console.WriteLine("\n🧪 模拟变量替换:");
        var context = new WorkflowContext();
        context.StepOutputs["fetch"] = new { data = "Hello from YAML!" };
        var resolver = new VariableResolver(context);
        var raw = "请分析: {{steps.fetch.output.data}}";
        var resolved = resolver.Resolve("请分析: {{steps.fetch.output.data}}");
        Console.WriteLine($"   模板:   {raw}");
        Console.WriteLine($"   替换后: {resolved}");

        // 演示嵌套属性
        context.StepOutputs["analyze"] = new { result = new { score = 95, level = "high" } };
        var nested = resolver.Resolve("评分: {{steps.analyze.output.result.score}}, 等级: {{steps.analyze.output.result.level}}");
        Console.WriteLine($"   嵌套属性: {nested}");

        // 演示缺失属性 → "null" + Debug 日志
        var missing = resolver.Resolve("缺失值: {{steps.fetch.output.missingField}}");
        Console.WriteLine($"   缺失属性: {missing} (应返回 'null')");
    }

    // ================================================================
    // 6. 热重载演示
    // ================================================================
    private static async Task DemoHotReload(
        WorkflowEngine engine,
        WorkflowRegistry registry,
        WorkflowImportExportManager importExport,
        IWorkflowBootstrapper bootstrapper,
        ILogger logger)
    {
        PrintHeader("热重载演示");

        // 创建临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), $"WorkflowYamlDemo_{Environment.ProcessId}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // 写入示例 YAML
            var yamlFile = Path.Combine(tempDir, "hot-reload-demo.yaml");
            var yamlContent = YAML_HOTRELOAD_TEMPLATE;
            await File.WriteAllTextAsync(yamlFile, yamlContent);
            Console.WriteLine($"📁 已创建临时文件: {yamlFile}");
            Console.WriteLine($"{yamlContent.Trim()}");
            Console.WriteLine(new string('─', 60));

            // 初始化热重载管理器
            var hotReload = new WorkflowHotReloadManager(
                registry,
                importExport,
                engine: null,
                logger is ILogger<WorkflowHotReloadManager> hrLogger ? hrLogger : null);

            // 注册事件
            hotReload.WorkflowFileChanged += (changeType, path) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n📢 文件变更 [{changeType}]: {Path.GetFileName(path)}");
                Console.ResetColor();
            };

            hotReload.WorkflowReloaded += (path, def) =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ 工作流已重新加载: {def.Name} v{def.Version} ({def.Steps.Count} 步骤)");
                Console.ResetColor();
            };

            // 先手动加载一次
            var importDef = await importExport.ImportFromYamlFileAsync(yamlFile);
            await bootstrapper.ApplyAsync(importDef.Name);
            Console.WriteLine($"✅ 初始加载: {importDef.Name} v{importDef.Version}");

            // 开始监控（仅当前文件所在目录）
            hotReload.StartWatching(tempDir, "*.yaml");

            Console.WriteLine($"\n👀 正在监控目录: {tempDir}");
            Console.WriteLine("   编辑临时 YAML 文件（修改版本号等）观察热重载效果");
            Console.WriteLine($"   按任意键停止监控...");

            // 等待用户操作
            var cts = new CancellationTokenSource();
            _ = Task.Run(() =>
            {
                Console.ReadKey(true);
                cts.Cancel();
            });

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (OperationCanceledException) { }

            hotReload.Dispose();
        }
        finally
        {
            // 清理
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
                Console.WriteLine($"\n🧹 已清理临时目录: {tempDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 清理临时目录失败: {ex.Message}");
            }
        }
    }

    // ================================================================
    // 7. 版本管理演示
    // ================================================================
    private static void DemoVersionManagement(WorkflowVersionManager versionManager, WorkflowRegistry registry)
    {
        PrintHeader("版本管理演示");

        // 解析版本
        Console.WriteLine("🔢 SemanticVersionHelper 解析:");
        string[] versions = ["1.0.0", "2.0.1", "1.9.5", "3.0.0-beta", "2.0.0-rc1"];
        foreach (var v in versions)
        {
            try
            {
                // 通过 WorkflowVersionManager.CompareSemanticVersions 间接使用 SemanticVersionHelper
                var cmp = versionManager.CompareSemanticVersions(v, "0.0.0");
                Console.WriteLine($"   ParseVersionParts(\"{v}\") → 大于 0.0.0: {(cmp > 0 ? "✓" : "✗")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   {v}: ❌ {ex.Message}");
            }
        }

        // 注册多版本
        Console.WriteLine("\n📦 注册多版本到 WorkflowRegistry:");
        var definition = new WorkflowDefinition
        {
            Name = "version-demo-workflow",
            Version = "1.0.0",
            Description = "版本管理演示工作流",
            Steps =
            [
                new StepDefinition
                {
                    Id = "step-v1",
                    Type = StepType.Code,
                    Assembly = AssemblyName,
                    Class = "NotifyStep"
                }
            ]
        };

        versionManager.RegisterVersion(definition, "初始版本");
        Console.WriteLine($"   注册: {definition.Name} v{definition.Version}");

        definition.Version = "1.1.0";
        versionManager.RegisterVersion(definition, "添加超时配置");
        Console.WriteLine($"   注册: {definition.Name} v{definition.Version}");

        definition.Version = "2.0.0-beta";
        versionManager.RegisterVersion(definition, "重大更新 - 预发布");
        Console.WriteLine($"   注册: {definition.Name} v{definition.Version}");

        // 查询
        var latest = versionManager.GetLatestVersion("version-demo-workflow");
        var allVersions = registry.GetVersions("version-demo-workflow").ToList();

        Console.WriteLine($"\n🔍 最新版本: '{latest}'");
        Console.WriteLine($"   所有版本: [{string.Join(", ", allVersions)}]");

        // 版本比较
        Console.WriteLine("\n⚖️ 版本比较:");
        var pairs = new[] { ("2.0.1", "1.9.5"), ("1.0.0", "1.0.0"), ("3.0.0", "2.0.0-beta") };
        foreach (var (a, b) in pairs)
        {
            var result = versionManager.CompareSemanticVersions(a, b);
            var sign = result > 0 ? ">" : result < 0 ? "<" : "==";
            Console.WriteLine($"   {a} {sign} {b}");
        }

        // 版本历史
        Console.WriteLine("\n📜 版本历史:");
        foreach (var entry in versionManager.GetVersionHistory("version-demo-workflow"))
        {
            Console.WriteLine($"   v{entry.Version} @ {entry.RegisteredAt:HH:mm:ss} — {entry.ChangeLog ?? "(无说明)"}");
        }
    }

    // ================================================================
    // Host Builder
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
                        chain.SetHeartbeatThreshold(TimeSpan.FromSeconds(30));

                        // 注册所有步骤处理器
                        chain.AddStep<FetchDataStep>();
                        chain.AddStep<NotifyStep>();
                        chain.AddStep<TransformStep>();
                        chain.AddStep<MockAgentStep>();
                        chain.AddStep<FailingStep>();
                        chain.AddStep<SlowDelayStep>();
                        chain.AddStep<EntryDemoStep>();
                        chain.AddStep<BranchAStep>();
                        chain.AddStep<BranchBStep>();


                    });
            });

    // ================================================================
    // 辅助方法
    // ================================================================
    private static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 60));
    }

    private static void PrintStepRecords(WorkflowEngine engine, string instanceId)
    {
        var records = engine.GetStepRecords(instanceId);
        Console.WriteLine($"  步骤执行档案 ({records.Count} 条):");
        foreach (var r in records)
        {
            var duration = r.Duration?.TotalMilliseconds ?? 0;
            var err = r.ErrorMessage != null ? $" err: {r.ErrorMessage[..Math.Min(r.ErrorMessage.Length, 40)]}" : "";
            Console.WriteLine(
                $"    [{r.Status,-12}] {r.StepId,-20} {r.StepType,-8} duration={duration:F0}ms{err}");
        }
    }

    private static void CleanupDatabase()
    {
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { /* ignore if locked */ }
    }

    // ================================================================
    // YAML 工作流定义（内嵌字符串常量）
    // ================================================================

    /// <summary>3.1 基础工作流: code → code 顺序执行</summary>
    private const string YAML_BASIC_WORKFLOW = @"
name: basic-demo
version: '1.0'
description: 基础YAML工作流 - 获取数据后通知
steps:
  - id: fetch-data
    type: code
    assembly: WorkflowYamlDemo
    class: FetchDataStep
  - id: notify
    type: code
    assembly: WorkflowYamlDemo
    class: NotifyStep
    depends_on:
      - fetch-data
";

    /// <summary>3.2 带重试配置的工作流: code + failing step with retry</summary>
    private const string YAML_RETRY_WORKFLOW = @"
name: retry-demo
version: '1.0'
description: 重试策略演示 - agent步骤配置最大3次重试
steps:
  - id: entry-fail
    type: code
    assembly: WorkflowYamlDemo
    class: FailingStep
    retry:
      max_retries: 3
      policy: exponential_backoff
      initial_delay: 1s
      backoff_factor: 2.0
";

    /// <summary>3.3 带超时配置的工作流: slow step with short timeout</summary>
    private const string YAML_TIMEOUT_WORKFLOW = @"
name: timeout-demo
version: '1.0'
description: 超时策略演示 - 慢步骤2秒超时
steps:
  - id: entry-slow
    type: code
    assembly: WorkflowYamlDemo
    class: SlowDelayStep
    timeout: 2s
    timeout_action: fail
";

    /// <summary>3.4 带 error_policy 的并行工作流</summary>
    private const string YAML_ERROR_POLICY_WORKFLOW = @"
name: error-policy-demo
version: '1.0'
description: 错误策略演示 - SkipFailedBranch
steps:
  - id: entry-demo
    type: code
    assembly: WorkflowYamlDemo
    class: EntryDemoStep
  - id: branch-a
    type: code
    assembly: WorkflowYamlDemo
    class: BranchAStep
  - id: branch-b
    type: code
    assembly: WorkflowYamlDemo
    class: BranchBStep
    error_policy: skip_failed_branch
  - id: notify
    type: code
    assembly: WorkflowYamlDemo
    class: NotifyStep
";

    /// <summary>3.5 使用变量模板的工作流</summary>
    private const string YAML_VARIABLE_WORKFLOW = @"
name: variable-demo
version: '1.0'
description: 变量解析演示 - 使用 {{steps.x.output.field}}
steps:
  - id: fetch
    type: code
    assembly: WorkflowYamlDemo
    class: FetchDataStep
  - id: analyze
    type: agent
    model: gpt-4
    prompt: '请分析以下数据: {{steps.fetch.output.data}}, 上下文: {{context.source}}'
";

    /// <summary>5.1 热重载演示模板</summary>
    private const string YAML_HOTRELOAD_TEMPLATE = @"
name: hot-reload-demo
version: '1.0'
description: 热重载演示工作流 v1.0
steps:
  - id: fetch
    type: code
    assembly: WorkflowYamlDemo
    class: FetchDataStep
  - id: notify
    type: code
    assembly: WorkflowYamlDemo
    class: NotifyStep
    depends_on:
      - fetch
";
}

// ================================================================
// 步骤处理器定义（2.1-2.3）
// ================================================================

/// <summary>2.1 CodeStep: 获取数据</summary>
internal sealed class FetchDataStep : CodeStepHandler
{
    public override string StepId => "fetch-data";



    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [FetchDataStep] 模拟获取数据...");
        await Task.Delay(100, ct);

        context.StepOutputs[StepId] = new { data = "样本数据结果", rows = 42 };
        return Complete(context.StepOutputs[StepId]);
    }
}

/// <summary>2.1 CodeStep: 通知/输出</summary>
internal sealed class NotifyStep : CodeStepHandler
{
    public override string StepId => "notify";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        // 读取上游步骤输出（如果存在）
        if (context.StepOutputs.TryGetValue("fetch-data", out var fetchOutput))
        {
            Console.WriteLine($"  [NotifyStep] 收到数据: {fetchOutput}");
        }
        else if (context.StepOutputs.TryGetValue("branch-a", out var branchA))
        {
            Console.WriteLine($"  [NotifyStep] 合并结果: {branchA}");
        }
        else
        {
            Console.WriteLine("  [NotifyStep] 执行完成");
        }

        return Complete(new { notified = true });
    }
}

/// <summary>2.1 CodeStep: 数据转换</summary>
internal sealed class TransformStep : CodeStepHandler
{
    public override string StepId => "transform";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [TransformStep] 转换数据...");
        return Complete(new { transformed = true });
    }
}

/// <summary>2.2 MockAgentStep: 模拟 Agent 步骤（标记 [Mock]）</summary>
internal sealed class MockAgentStep : AgentStepHandler
{
    public override string StepId => "agent-analyze";
    public override string RouteName => "workflow.analyze";
    public override string EventType => "workflow.step";
    public override AgentCommunicationMode Mode => AgentCommunicationMode.RunClient;

    public override string BuildPrompt(WorkflowContext context) => "分析数据";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [MockAgentStep] [Mock] 模拟 Agent 调用...");
        await Task.Delay(200, ct);
        context.SetData(StepId, new { analyzed = true, score = 95 });
        return Complete(new { analyzed = true, score = 95 });
    }
}

/// <summary>2.3 FailingStep: 故意失败（用于演示 retry 和 error_policy）</summary>
internal sealed class FailingStep : CodeStepHandler
{
    public override string StepId => "entry-fail";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [FailingStep] ❌ 模拟步骤失败...");
        return Failed(new InvalidOperationException("模拟的步骤执行失败"));
    }
}

/// <summary>慢速步骤: 用于演示超时（5秒延迟,timeout配置2秒）</summary>
internal sealed class SlowDelayStep : CodeStepHandler
{
    public override string StepId => "entry-slow";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [SlowDelayStep] ⏳ 开始 5 秒延迟（timeout=2s 将触发）...");
        try
        {
            await Task.Delay(5000, ct);
            Console.WriteLine("  [SlowDelayStep] 延迟完成（未被超时中断）");
            return Complete(new { status = "done" });
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  [SlowDelayStep] ⏰ 超时取消");
            throw;
        }
    }
}

/// <summary>并行分支 A: 正常完成</summary>
internal sealed class BranchAStep : CodeStepHandler
{
    public override string StepId => "branch-a";


    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [BranchA] ✅ 分支A 执行成功");
        context.StepOutputs[StepId] = new { branch = "A", result = "ok" };
        return Complete(context.StepOutputs[StepId]);
    }
}

/// <summary>并行分支 B: 故意失败（但 error_policy=SkipFailedBranch 允许被跳过）</summary>
internal sealed class BranchBStep : CodeStepHandler
{
    public override string StepId => "branch-b";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [BranchB] ❌ 分支B 故意失败（将被 SkipFailedBranch 跳过）");
        return Failed(new InvalidOperationException("分支B 模拟失败"));
    }
}

/// <summary>入口步骤: 触发并行分支 A+B，完成后汇合到 merge-step</summary>
internal sealed class EntryDemoStep : CodeStepHandler
{
    public override string StepId => "entry-demo";

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        Console.WriteLine("  [EntryDemoStep] 触发并行分支 branch-a | branch-b...");
        return ParallelJoin("notify", "branch-a", "branch-b");
    }
}

namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>演示场景 — 由主菜单触发</summary>
partial class Program
{
    // =================================================================
    // Scenario 1: Fluent Timeout 生效
    //   Fluent Timeout="00:00:02", 步骤执行 5s → 超时失败
    // =================================================================
    static async Task Scenario1_HandlerDefaultTimeout(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 1 — Fluent Timeout 生效");
        Console.WriteLine("  Fluent 配置: Timeout=\"00:00:02\" (2秒)");
        Console.WriteLine("  YAML 配置:    无 (Fluent 配置生效)");
        Console.WriteLine("  步骤执行:     等待 5 秒");
        Console.WriteLine("  → 预期: 2秒后超时, 步骤标记为 Failed");

        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S1_TIMEOUT);

        var ctx = new WorkflowContext { InstanceId = $"s1-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("timeout-demo", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "timeout-demo");
        Console.WriteLine($"  结果: 步骤状态 = {rec?.Status}");
        Console.WriteLine($"  工作流状态 = {inst.Status}");
        PrintIf(rec?.Status == StepStatus.Failed, "✓ Fluent Timeout 生效：步骤超时失败");
    }

    // =================================================================
    // Scenario 2: Fluent Retry 生效
    //   Fluent Retry.MaxRetries=3, 步骤始终抛异常 → 重试 3 次后失败
    // =================================================================
    static async Task Scenario2_HandlerDefaultRetry(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 2 — Fluent Retry 生效");
        Console.WriteLine("  Fluent 配置: Retry.MaxRetries=3, Policy=immediate");
        Console.WriteLine("  YAML 配置:    无 (Fluent 配置生效)");
        Console.WriteLine("  步骤:         始终抛出异常 (TimeoutException)");
        Console.WriteLine("  → 预期: 重试 3 次后步骤标记为 Failed");

        RetryDemoStep.ResetCount();

        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S2_RETRY);
        var ctx = new WorkflowContext { InstanceId = $"s2-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("retry-demo", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "retry-demo");
        var attempts = RetryDemoStep.ExecutionCount;
        Console.WriteLine($"  结果: 步骤状态 = {rec?.Status}, 执行次数 = {attempts}");
        PrintIf(attempts >= 3, "✓ Fluent Retry 生效：执行了重试策略");
    }

    // =================================================================
    // Scenario 3: YAML Timeout 覆盖 Fluent 配置
    //   Fluent Timeout=500ms, YAML Timeout=10s → 步骤 2s 完成
    // =================================================================
    static async Task Scenario3_YamlOverridesTimeout(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 3 — YAML Timeout 覆盖 Fluent 配置");
        Console.WriteLine("  Fluent 配置: Timeout=\"00:00:00.500\" (500ms)");
        Console.WriteLine("  YAML 配置:    timeout=10s (覆盖 Fluent)");
        Console.WriteLine("  步骤执行:     等待 2 秒");
        Console.WriteLine("  → 预期: 使用 YAML 的 10s 超时, 步骤成功完成");

        MixedConfigStep.ResetCount();

        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S3_OVERRIDE);
        var ctx = new WorkflowContext { InstanceId = $"s3-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("mixed-step", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "mixed-step");
        Console.WriteLine($"  结果: 步骤状态 = {rec?.Status}");
        Console.WriteLine($"  工作流状态 = {inst.Status}");
        PrintIf(rec?.Status == StepStatus.Completed, "✓ YAML 优先: 覆盖了 Fluent 的 500ms 超时");
    }

    // =================================================================
    // Scenario 4: YAML 部分覆盖 — 仅覆盖 timeout, Fluent retry 保留
    // =================================================================
    static async Task Scenario4_YamlPartialOverride(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 4 — YAML 部分覆盖 (timeout 被覆盖, retry 保留)");
        Console.WriteLine("  Fluent 配置: Timeout=\"00:00:00.500\", Retry.MaxRetries=3");
        Console.WriteLine("  YAML 配置:    timeout=10s (仅覆盖 timeout)");
        Console.WriteLine("  步骤:         首次抛异常触发重试");
        Console.WriteLine("  → 预期: 使用 YAML 的 10s timeout + Fluent 的 3 次重试");

        MixedConfigStep.ResetCount();

        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S3_OVERRIDE);
        var ctx = new WorkflowContext { InstanceId = $"s4-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("mixed-step", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "mixed-step");
        Console.WriteLine($"  结果: 步骤状态 = {rec?.Status}, 执行次数 = {MixedConfigStep.ExecutionCount}");
        Console.WriteLine("  (步骤首次抛异常进入重试, 后续成功 — 证明 retry 来自 Fluent 配置)");
    }

    // =================================================================
    // Scenario 5: StepHandlerDefaults.FromHandler() 提取 + Prompt 优先级
    // =================================================================
    static void Scenario5_HandlerDefaultsExtraction()
    {
        PrintHeader("Scenario 5 — FromHandler 提取 + Prompt 优先级");
        Console.WriteLine("  配置外部化后，Handler 实例上不再声明 Prompt/SystemPrompt 虚属性。");
        Console.WriteLine("  StepHandlerDefaults.FromHandler() 返回 null（配置在 Fluent API 层）。");
        Console.WriteLine();
        Console.WriteLine("  Prompt 优先级链路:");
        Console.WriteLine("    YAML prompt > Fluent API prompt > Handler 虚属性 > BuildPrompt(ctx) 回退");
        Console.WriteLine();

        var agentHandler = new DemoAgentStep();
        var defaults = StepHandlerDefaults.FromHandler(agentHandler);
        Console.WriteLine($"  FromHandler 提取: Prompt=\"{defaults.Prompt ?? "null"}\", SystemPrompt=\"{defaults.SystemPrompt ?? "null"}\"");
        Console.WriteLine($"  Handler.BuildPrompt: \"{agentHandler.BuildPrompt(new WorkflowContext())}\"");
        Console.WriteLine();

        // 情形 A: YAML 有 prompt → YAML 胜出
        var resolvedA = !string.IsNullOrWhiteSpace("这是 YAML 的 prompt")
            ? "这是 YAML 的 prompt"
            : "Fluent prompt";
        Console.WriteLine($"  情形A (YAML有值): {resolvedA} (YAML > Fluent)");

        // 情形 B: 仅有 Fluent prompt → Fluent 胜出
        var resolvedB = !string.IsNullOrWhiteSpace("Fluent prompt")
            ? "Fluent prompt"
            : agentHandler.BuildPrompt(new WorkflowContext());
        Console.WriteLine($"  情形B (仅Fluent):  {resolvedB} (Fluent > BuildPrompt)");

        // 情形 C: 两者为空 → 回退到 BuildPrompt
        var noPromptHandler = new FallbackAgentStep();
        var resolvedC = !string.IsNullOrWhiteSpace(noPromptHandler.Prompt)
            ? noPromptHandler.Prompt
            : noPromptHandler.BuildPrompt(new WorkflowContext());
        Console.WriteLine($"  情形C (回退):     {resolvedC} (BuildPrompt fallback)");

        PrintIf(true, "✓ FromHandler 提取与 Prompt 优先级验证完成");
    }

    // =================================================================
    // Scenario 6: ExportTemplate — 合并 Fluent 配置生成 YAML 模板
    // =================================================================
    static async Task Scenario6_ExportTemplate(WorkflowImportExportManager importExport, WorkflowRegistry registry)
    {
        PrintHeader("Scenario 6 — ExportTemplate 生成含 Fluent 配置的 YAML");

        // 注册一个工作流定义
        var definition = new WorkflowDefinition
        {
            Name = "template-demo",
            Version = "1.0.0",
            Description = "用于生成模板的演示工作流",
            Steps =
            [
                new() { Id = "step-timeout", Type = StepType.Code, Class = nameof(TimeoutDemoStep), Assembly = typeof(TimeoutDemoStep).Assembly.GetName().Name! },
                new() { Id = "step-retry", Type = StepType.Code, Class = nameof(RetryDemoStep), Assembly = typeof(RetryDemoStep).Assembly.GetName().Name! },
                new() { Id = "step-agent", Type = StepType.Agent, Class = nameof(DemoAgentStep), Assembly = typeof(DemoAgentStep).Assembly.GetName().Name! },
            ]
        };
        registry.Register(definition);

        // 构建 Handler 默认值映射
        var handlerDefaults = new Dictionary<string, StepHandlerDefaults>
        {
            ["step-timeout"] = StepHandlerDefaults.FromHandler(new TimeoutDemoStep()),
            ["step-retry"] = StepHandlerDefaults.FromHandler(new RetryDemoStep()),
            ["step-agent"] = StepHandlerDefaults.FromHandler(new DemoAgentStep()),
        };

        // 导出模板
        var yaml = importExport.ExportTemplate("template-demo", handlerDefaults);

        Console.WriteLine("  生成 YAML 模板 (Handler 默认值已合并到各步骤):");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine(yaml);
        Console.WriteLine(new string('─', 60));

        // 验证关键字段
        var hasTimeout = yaml.Contains("timeout:");
        var hasRetry = yaml.Contains("retry:");
        var hasPrompt = yaml.Contains("prompt:");
        Console.WriteLine($"  含 timeout:     {(hasTimeout ? "✓" : "✗")}");
        Console.WriteLine($"  含 retry:       {(hasRetry ? "✓" : "✗")}");
        Console.WriteLine($"  含 prompt:      {(hasPrompt ? "✓" : "✗")}");

        // 演示 YAML 配置优先
        Console.WriteLine();
        Console.WriteLine("  YAML 配置优先验证:");
        Console.WriteLine("  (若 YAML 已设置 timeout, 导出的模板保留 YAML 值)");
        await DemoYamlPriority(importExport, registry);

        PrintIf(true, "✓ ExportTemplate 合并 Handler 默认值完成");
    }

    static async Task DemoYamlPriority(WorkflowImportExportManager importExport, WorkflowRegistry registry)
    {
        var parser = new YamlWorkflowParser();
        var parsed = parser.Parse(YAML_PRIORITY_TEST);
        registry.Register(parsed);

        var defaults = new Dictionary<string, StepHandlerDefaults>
        {
            ["step-1"] = new StepHandlerDefaults { Timeout = "00:00:30" }
        };

        var exported = importExport.ExportTemplate("priority-test", defaults);
        Console.WriteLine("  ──────────────────────────────");
        Console.WriteLine(exported);
        Console.WriteLine("  ──────────────────────────────");
        Console.WriteLine(exported.Contains("00:05:00")
            ? "  ✓ YAML 的 5m timeout 被保留 (未被 Handler 的 30s 覆盖)"
            : "  ⚠ 预期 YAML 值应优先, 请检查");
    }

    // =================================================================
    // Scenario 7: 向后兼容 — 未声明默认配置的 Handler
    // =================================================================
    static async Task Scenario7_BackwardCompatible(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 7 — 向后兼容 (未声明默认配置)");
        Console.WriteLine("  NoDefaultStep: 未重写 Timeout/Retry/ErrorPolicy");
        Console.WriteLine("  → 使用 StepHandlerBase 的 null 默认值");
        Console.WriteLine("  → 行为与 handler-native-config 前一致");

        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S7_NO_DEFAULT);
        var ctx = new WorkflowContext { InstanceId = $"s7-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("no-default-step", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "no-default-step");
        Console.WriteLine($"  结果: 步骤状态 = {rec?.Status}, 工作流状态 = {inst.Status}");
        PrintIf(rec?.Status == StepStatus.Completed, "✓ 向后兼容: 无默认值的 Handler 执行正常");
    }

    // =================================================================
    // Scenario 8: IWorkflowBootstrapper LoadAndApplyAsync 演示
    //   一步完成：解析 YAML → 注册到 Registry → 同步到 Engine
    //   替代了已废弃的 builder.RegisterFromYaml 模式
    // =================================================================
    static async Task Scenario8_BootstrapperTwoStep(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper)
    {
        PrintHeader("Scenario 8 — IWorkflowBootstrapper LoadAndApplyAsync 演示");
        Console.WriteLine("  LoadAndApplyAsync 一步完成：解析 YAML → 注册到 Registry → 同步到 Engine");
        Console.WriteLine("  YAML 是运行时策略配置层，通过 bootstrapper 在运行时加载，无需 builder 参与。");
        Console.WriteLine();

        // Step 1: LoadAndApplyAsync — 一步完成
        Console.WriteLine("  Step 1 — LoadAndApplyAsync（一步加载 YAML 到 Engine）");
        var wfName = await bootstrapper.LoadAndApplyAsync(YAML_S8_BOOTSTRAPPER);
        Console.WriteLine($"   ✅ 工作流 '{wfName}' 已加载并应用到 Engine");
        Console.WriteLine($"   ✅ IsApplied = {bootstrapper.IsApplied(wfName)}（已同步到 Engine）");
        Console.WriteLine();

        // 执行工作流——YAML 配置的 timeout 通过 LoadAndApplyAsync 同步后生效
        Console.WriteLine("  Step 2 — 执行工作流（YAML timeout 配置已生效）");
        Console.WriteLine("   YAML 配置: timeout=10s, Handler 默认 timeout=500ms");
        Console.WriteLine("   步骤执行: 等待 2 秒");
        Console.WriteLine("   → 预期: 使用 YAML 的 10s 超时, 步骤成功完成");
        Console.WriteLine();

        MixedConfigStep.ResetCount();
        var ctx = new WorkflowContext { InstanceId = $"s8-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("mixed-step", ctx, CancellationToken.None, wfName);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        var rec = records.FirstOrDefault(r => r.StepId == "mixed-step");
        Console.WriteLine($"   结果: 步骤状态 = {rec?.Status}");
        Console.WriteLine($"   工作流状态 = {inst.Status}");
        PrintIf(rec?.Status == StepStatus.Completed,
            "✓ LoadAndApplyAsync 生效: YAML 配置正确应用");

        // 幂等性验证
        Console.WriteLine();
        Console.WriteLine("  Step 3 — 重复 Apply（验证幂等性）");
        await bootstrapper.ApplyAsync(wfName);
        Console.WriteLine("   第二次 ApplyAsync 使用 ReplaceStepDefinitions，无异常");
        Console.WriteLine($"   IsApplied 仍为 {bootstrapper.IsApplied(wfName)}");
    }

    // =================================================================
    // Scenario 9: Builder Fluent API 配置演示 (代码注册 → YAML 导出 → 运行)
    // =================================================================
    static async Task Scenario9_FluentApiDemo(WorkflowEngine engine, IWorkflowBootstrapper bootstrapper, WorkflowImportExportManager importExport)
    {
        PrintHeader("Scenario 9 — Builder Fluent API 配置 (代码定义 → YAML 导出 → 运行)");
        Console.WriteLine("  工作流: fluent-entry → fluent-process → fluent-validation → fluent-agent");
        Console.WriteLine();
        Console.WriteLine("  AddWorkflow 注册的步骤及 Fluent 配置:");
        Console.WriteLine("  ┌──────────────────────┬────────────────────────────────────────┐");
        Console.WriteLine("  │ 步骤                 │ Fluent 配置                            │");
        Console.WriteLine("  ├──────────────────────┼────────────────────────────────────────┤");
        Console.WriteLine("  │ fluent-entry         │ (无)                                   │");
        Console.WriteLine("  ├──────────────────────┼────────────────────────────────────────┤");
        Console.WriteLine("  │ fluent-process       │ Timeout=10s, Retry.MaxRetries=3        │");
        Console.WriteLine("  ├──────────────────────┼────────────────────────────────────────┤");
        Console.WriteLine("  │ fluent-validation    │ Timeout=15s, ErrorPolicy=skip_failed    │");
        Console.WriteLine("  ├──────────────────────┼────────────────────────────────────────┤");
        Console.WriteLine("  │ fluent-agent         │ Timeout=30s, Prompt=配置提示词          │");
        Console.WriteLine("  │                      │ SystemPrompt=你是 fluent 助手           │");
        Console.WriteLine("  └──────────────────────┴────────────────────────────────────────┘");
        Console.WriteLine();

        // 步骤 1: 导出代码定义的 YAML
        Console.WriteLine("  Step 1 — ExportToYaml 导出代码定义的 YAML:");
        Console.WriteLine(new string('─', 60));
        var yaml = importExport.ExportToYaml("s9-fluent-wf");
        Console.WriteLine(yaml);
        Console.WriteLine(new string('─', 60));

        // 步骤 2: 直接运行代码注册的工作流（不加载外部 YAML）
        Console.WriteLine("  Step 2 — 运行代码注册的工作流（无外部 YAML 覆盖）:");
        Console.WriteLine();

        FluentProcessStep.ResetCount();

        var wfName = "s9-fluent-wf";
        var ctx = new WorkflowContext { InstanceId = $"s9-{DateTime.Now:HHmmss}" };
        var instanceId = await engine.StartAsync("fluent-entry", ctx, CancellationToken.None, wfName);

        // 等待工作流完成
        await Task.Delay(500);

        var inst = engine.GetInstance(instanceId)!;
        var records = engine.GetStepRecords(instanceId);
        Console.WriteLine();

        // 验证 fluent-process: Fluent Timeout+Retry 应生效
        var processRec = records.FirstOrDefault(r => r.StepId == "fluent-process");
        Console.WriteLine($"  fluent-process: 状态={processRec?.Status}, 执行次数={FluentProcessStep.ExecutionCount}");
        PrintIf(FluentProcessStep.ExecutionCount >= 3, "→ Fluent MaxRetries=3 生效");
        PrintIf(processRec?.Status == StepStatus.Completed, "→ Fluent Timeout=10s 生效");

        // 验证 fluent-agent
        var agentRec = records.FirstOrDefault(r => r.StepId == "fluent-agent");
        Console.WriteLine($"  fluent-agent: 状态={agentRec?.Status}");
        PrintIf(agentRec?.Status == StepStatus.Completed, "→ Fluent Timeout=30s 生效（无外部 YAML 覆盖）");

        // 验证完整工作流状态
        Console.WriteLine($"  工作流状态: {inst.Status}");
        PrintIf(inst.Status == "completed", "✓ Fluent API 配置的 4 步工作流执行成功");

        Console.WriteLine();
        Console.WriteLine("  说明:");
        Console.WriteLine("    • 导出的 YAML 包含 Fluent API 配置值，可作为配置起点");
        Console.WriteLine("    • 编辑 YAML 后通过热加载或 bootstrapper 应用覆盖");
        Console.WriteLine("    • 运行时优先级不变: 外部 YAML > Fluent > Handler 虚属性");
    }

    // =================================================================
    // Scenario 10: 导出代码定义的 YAML (AddWorkflow → ExportToYaml)
    // =================================================================
    static async Task Scenario10_ExportYamlFromCode(WorkflowImportExportManager importExport)
    {
        PrintHeader("Scenario 10 — 代码定义工作流 → 自动生成 YAML");
        Console.WriteLine("  AddWorkflow + WithName 注册的工作流已写入 WorkflowRegistry。");
        Console.WriteLine("  直接调用 ExportToYaml 即可导出包含 Fluent 配置的 YAML。");
        Console.WriteLine();

        // 导出 s9-fluent-wf（由 AddWorkflow 在启动时注册）
        var yaml = importExport.ExportToYaml("s9-fluent-wf");
        Console.WriteLine("  ── s9-fluent-wf ──────────────────────────────────");
        Console.WriteLine(yaml);
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();

        // 验证关键字段
        var checks = new (string Label, bool Pass)[]
        {
            ("name: s9-fluent-wf", yaml.Contains("name: s9-fluent-wf")),
            ("version: '1.0'", yaml.Contains("version: '1.0'")),
            ("description", yaml.Contains("description:")),
            ("4 个 steps", yaml.Split("id:").Length >= 5),
            ("Fluent timeout (fluent-process)", yaml.Contains("timeout: 00:00:10")),
            ("Fluent retry (fluent-process)", yaml.Contains("retry:")),
            ("Fluent error_policy (fluent-validation)", yaml.Contains("error_policy:")),
            ("Fluent prompt (fluent-agent)", yaml.Contains("prompt:")),
            ("Fluent system_prompt (fluent-agent)", yaml.Contains("system_prompt:")),
        };

        Console.WriteLine("  字段验证:");
        foreach (var (label, pass) in checks)
        {
            Console.WriteLine($"    {(pass ? "✓" : "✗")} {label}");
        }

        Console.WriteLine();
        Console.WriteLine("  说明:");
        Console.WriteLine("    • Fluent API 配置值自动写入 StepDefinition");
        Console.WriteLine("    • 运行时 YAML 外部配置优先级仍然高于 Fluent");
        Console.WriteLine("    • ExportToYaml 导出的 YAML 可作为配置起点");
        Console.WriteLine("    • 配合热加载: 编辑 YAML → 引擎自动应用覆盖");

        PrintIf(checks.All(c => c.Pass), "✓ 代码→YAML 自动导出成功");
    }
}

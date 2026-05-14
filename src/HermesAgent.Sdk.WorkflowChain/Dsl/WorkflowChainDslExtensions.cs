using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// DSL Workflow 注册扩展方法。
/// 提供 <c>Register&lt;T&gt;()</c> 在 <see cref="WorkflowChainBuilder"/> 上注册类继承式工作流定义。
/// 优先从 YAML 文件加载，不存在或加载失败时回退到代码构建。
/// </summary>
public static class WorkflowChainDslExtensions
{
    private static readonly YamlWorkflowParser Parser = new();

    private const string LogPrefix = "[Hermes.WorkflowChain]";
    private const string YamlFilePattern = "{0}-{1}.yaml";

    /// <summary>
    /// 注册一个 DSL 工作流。优先从 YAML 文件加载 WorkflowDefinition，
    /// 不存在或加载失败时回退到代码构建。
    ///
    /// YAML 路径规则：<c>{YamlConfigDirectory}/{Name}-{Version}.yaml</c>
    /// YAML 加载校验链路：解析 → 结构校验 → Name+Version 匹配 → Handler 存在性（警告）。
    /// 任何校验失败都会回退到代码构建，不阻断启动。
    /// </summary>
    /// <typeparam name="TWorkflow">工作流类型（必须有无参构造函数）</typeparam>
    /// <param name="builder">工作流链式构建器</param>
    /// <param name="logger">可选的日志记录器。传入后生产环境可观测加载链路。</param>
    /// <exception cref="InvalidOperationException">
    /// 工作流名称重复、步骤定义不合法时抛出。
    /// </exception>
    public static void Register<TWorkflow>(
        this WorkflowChainBuilder builder,
        ILogger? logger = null)
        where TWorkflow : Workflow, new()
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        var workflow = new TWorkflow();

        if (string.IsNullOrWhiteSpace(workflow.Name))
            throw new InvalidOperationException($"工作流 {typeof(TWorkflow).Name}.Name 返回空值");

        if (string.IsNullOrWhiteSpace(workflow.Version))
            throw new InvalidOperationException($"工作流 {typeof(TWorkflow).Name}.Version 返回空值");

        if (builder.HasWorkflowDefinition(workflow.Name))
            throw new InvalidOperationException(
                $"工作流 \"{workflow.Name}\" 已通过 AddWorkflow 注册，不允许重复注册");

        // ALWAYS 调用 Build 注册 Handler 到 DI（无论从 YAML 还是代码加载）
        // Handler 承载实际步骤执行逻辑，YAML 仅提供运行时策略（timeout/retry 等）
        var stepBuilder = new DslStepBuilder(builder);
        workflow.Build(stepBuilder);

        // 尝试从 YAML 加载
        var configDir = builder.Options.YamlConfigDirectory;
        if (string.IsNullOrWhiteSpace(configDir))
        {
            logger?.LogDebug("{Prefix} YamlConfigDirectory 未配置，跳过 YAML 加载", LogPrefix);
            stepBuilder.BuildDefinition(
                workflow.Id,
                workflow.Name,
                workflow.Version,
                workflow.Description);
            return;
        }

        // 防止路径穿越：Name/Version 中不允许包含 ..
        var fileName = string.Format(YamlFilePattern, workflow.Name, workflow.Version);
        if (fileName.Contains(".."))
        {
            throw new InvalidOperationException(
                $"非法的工作流名称或版本: \"{workflow.Name}\" / \"{workflow.Version}\"");
        }

        var yamlPath = Path.Combine(configDir, fileName);

        if (File.Exists(yamlPath)
            && TryLoadFromYaml(
                builder,
                yamlPath,
                workflow.Name,
                workflow.Version,
                logger,
                out var parsed))
        {
            // YAML 加载成功
            builder.AddWorkflowDefinition(parsed!);
            logger?.LogInformation(
                "{Prefix} 已从 YAML 加载工作流: {WorkflowName} v{Version} ({Path})",
                LogPrefix,
                workflow.Name,
                workflow.Version,
                yamlPath);
            return;
        }

        // 回退到代码构建（YAML 不存在或加载失败）
        logger?.LogDebug(
            "{Prefix} 回退到代码构建工作流: {WorkflowName} v{Version}",
            LogPrefix,
            workflow.Name,
            workflow.Version);

        stepBuilder.BuildDefinition(
            workflow.Id,
            workflow.Name,
            workflow.Version,
            workflow.Description);
    }

    /// <summary>
    /// 尝试从 YAML 文件加载 WorkflowDefinition。
    /// 解析 → 结构校验 → Name+Version 匹配 → Handler 存在性（警告）。
    /// 任何校验失败均返回 false，不抛异常，调用方回退到代码构建。
    /// </summary>
    /// <returns>加载成功返回 true，失败返回 false（不抛异常）。</returns>
    private static bool TryLoadFromYaml(
        WorkflowChainBuilder builder,
        string yamlPath,
        string expectedName,
        string expectedVersion,
        ILogger? logger,
        out WorkflowDefinition? definition)
    {
        definition = null;

        // Step 1: 解析 YAML（不执行自动校验，改为分阶段校验）
        WorkflowDefinition? parsed;
        try
        {
            parsed = Parser.ParseFromFile(yamlPath, validate: false);
        }
        catch (FileNotFoundException)
        {
            logger?.LogDebug("{Prefix} YAML 文件不存在: {Path}", LogPrefix, yamlPath);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(
                "{Prefix} YAML 格式解析失败: {Path}, {Error}",
                LogPrefix,
                yamlPath,
                ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger?.LogWarning(ex, "{Prefix} YAML 加载异常: {Path}", LogPrefix, yamlPath);
            return false;
        }

        if (parsed == null)
        {
            logger?.LogWarning("{Prefix} YAML 解析结果为 null: {Path}", LogPrefix, yamlPath);
            return false;
        }

        // Step 2: 结构校验
        var validationErrors = parsed.Validate();
        if (validationErrors.Count > 0)
        {
            logger?.LogWarning(
                "{Prefix} YAML 结构校验失败: {Path}\n  {Errors}",
                LogPrefix,
                yamlPath,
                string.Join("\n  ", validationErrors.Select(e => $"- {e}")));
            return false;
        }

        // Step 3: Name+Version 匹配校验
        if (!string.Equals(parsed.Name, expectedName, StringComparison.Ordinal)
            || !string.Equals(parsed.Version, expectedVersion, StringComparison.Ordinal))
        {
            logger?.LogWarning(
                "{Prefix} YAML Name+Version 不匹配: {Path}, "
                + "期望 {ExpectedName}-{ExpectedVersion}, 实际 {ActualName}-{ActualVersion}",
                LogPrefix,
                yamlPath,
                expectedName,
                expectedVersion,
                parsed.Name,
                parsed.Version);
            return false;
        }

        // Step 4: Handler 存在性校验（仅警告，不阻断加载）
        var registeredStepIds = builder.GetHandlerStepIds();
        var missingHandlers = parsed.Steps
            .Select(s => s.Id)
            .Where(id => !registeredStepIds.Contains(id))
            .ToList();

        if (missingHandlers.Count > 0)
        {
            logger?.LogWarning(
                "{Prefix} YAML 工作流 \"{WorkflowName}\" 中步骤 Handler 未注册: {MissingHandlers}。"
                + "这些步骤的 timeout/retry 策略不生效",
                LogPrefix,
                parsed.Name,
                string.Join(", ", missingHandlers));
        }

        // 所有校验通过
        definition = parsed;
        return true;
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// IWorkflowBootstrapper 默认实现。
///
/// 职责：将 YAML 工作流定义加载到 Registry 并同步到 Engine 的 _stepDefinitions。
/// 提供了一步式的 LoadAndApplyAsync，替代了已废弃的 builder.RegisterFromYaml。
///
/// YAML 是 C# Handler 的运行时策略配置，通过热重载机制生效：
/// - 启动阶段：bootstrapper 将初始 YAML 定义同步到 Engine
/// - 运行阶段：HotReload 监听文件变化后通过 ReplaceStepDefinitions 更新 Engine
/// </summary>
public sealed class WorkflowBootstrapper : IWorkflowBootstrapper
{
    private readonly WorkflowRegistry _registry;
    private readonly WorkflowEngine _engine;
    private readonly YamlWorkflowParser _parser;
    private readonly ILogger<WorkflowBootstrapper>? _logger;
    private readonly ConcurrentDictionary<string, bool> _applied = new(StringComparer.OrdinalIgnoreCase);

    public WorkflowBootstrapper(
        WorkflowRegistry registry,
        WorkflowEngine engine,
        YamlWorkflowParser? parser = null,
        ILogger<WorkflowBootstrapper>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _parser = parser ?? new YamlWorkflowParser();
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ApplyAsync(string workflowName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(workflowName))
            throw new ArgumentException("工作流名称不能为空", nameof(workflowName));

        ct.ThrowIfCancellationRequested();

        var definition = _registry.Get(workflowName);

        if (_applied.TryGetValue(workflowName, out _))
        {
            _logger?.LogDebug("替换工作流步骤定义: {WorkflowName}", workflowName);
            _engine.ReplaceStepDefinitions(workflowName, definition.Steps);
        }
        else
        {
            _logger?.LogDebug("注册工作流步骤定义: {WorkflowName}", workflowName);
            _engine.RegisterStepDefinitions(workflowName, definition.Steps);
        }

        _applied[workflowName] = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ApplyAllAsync(CancellationToken ct = default)
    {
        var names = _registry.GetRegisteredNames().ToList();
        _logger?.LogInformation("应用所有已注册工作流，共 {Count} 个", names.Count);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            await ApplyAsync(name, ct);
        }

        _logger?.LogInformation("所有工作流已应用到 Engine");
    }

    /// <inheritdoc />
    public bool IsApplied(string workflowName)
    {
        return _applied.ContainsKey(workflowName);
    }

    /// <inheritdoc />
    public async Task<string> LoadAndApplyAsync(string yamlContent, string? version = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(yamlContent))
            throw new ArgumentException("YAML 内容不能为空", nameof(yamlContent));

        ct.ThrowIfCancellationRequested();

        var definition = _parser.Parse(yamlContent);
        if (version != null)
            definition.Version = version;

        _registry.Register(definition);
        await ApplyAsync(definition.Name, ct);

        _logger?.LogInformation("已从 YAML 内容加载并应用工作流: {WorkflowName} v{Version}",
            definition.Name, definition.Version);

        return definition.Name;
    }

    /// <inheritdoc />
    public async Task<string> LoadAndApplyFromFileAsync(string filePath, string? version = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        ct.ThrowIfCancellationRequested();

        var definition = await _parser.ParseFromFileAsync(filePath);
        if (version != null)
            definition.Version = version;

        _registry.Register(definition);
        await ApplyAsync(definition.Name, ct);

        _logger?.LogInformation("已从 YAML 文件加载并应用工作流: {WorkflowName} v{Version} ({FilePath})",
            definition.Name, definition.Version, filePath);

        return definition.Name;
    }
}

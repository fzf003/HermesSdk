using System.Text.Json;
using YamlDotNet.Serialization;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// Handler 默认步骤策略与输入模板（用于导出模板时合并到 YAML）。
/// 这些值对应 StepHandlerBase / AgentStepHandler 上的虚属性默认值。
/// </summary>
public record StepHandlerDefaults
{
    public string? Timeout { get; init; }
    public string? TimeoutAction { get; init; }
    public RetryConfigYaml? Retry { get; init; }
    public string? ErrorPolicy { get; init; }
    public string? Prompt { get; init; }
    public string? SystemPrompt { get; init; }
    public string? RouteName { get; init; }
    public string? EventType { get; init; }
    public ApprovalNotificationConfig? Notification { get; init; }
    public string? HeartbeatExtension { get; init; }

    /// <summary>从 StepHandlerBase 提取通用步骤策略默认值</summary>
    public static StepHandlerDefaults FromHandler(StepHandlerBase handler)
    {
        var agentHandler = handler as AgentStepHandler;
        return new StepHandlerDefaults
        {
            Timeout = handler.Timeout,
            TimeoutAction = handler.TimeoutAction,
            Retry = handler.Retry,
            ErrorPolicy = handler.ErrorPolicy,
            Prompt = agentHandler?.Prompt,
            SystemPrompt = agentHandler?.SystemPrompt,
            RouteName = agentHandler?.RouteName,
            EventType = agentHandler?.EventType,
            HeartbeatExtension = handler.HeartbeatExtension?.ToString(),
        };
    }
}

/// <summary>
/// 工作流导入导出管理器 - 支持YAML和JSON格式的序列化/反序列化。
/// </summary>
public class WorkflowImportExportManager
{
    private readonly WorkflowRegistry _registry;
    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public WorkflowImportExportManager(WorkflowRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        _yamlSerializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithTypeConverter(new StepTypeConverter())
            .WithTypeConverter(new RetryPolicyConverter())
            .WithTypeConverter(new RetryConfigConverter())
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .WithTypeConverter(new StepTypeConverter())
            .WithTypeConverter(new RetryPolicyConverter())
            .WithTypeConverter(new RetryConfigConverter())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// 将工作流定义导出为YAML字符串。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号(可选,默认最新版本)</param>
    /// <returns>YAML格式的工作流定义</returns>
    public string ExportToYaml(string name, string? version = null)
    {
        var definition = version != null
            ? _registry.GetByVersion(name, version)
            : _registry.Get(name);

        return _yamlSerializer.Serialize(definition);
    }

    /// <summary>
    /// 导出包含 Handler 默认配置的初始 YAML 模板。
    /// 将 Handler 声明的默认策略（timeout/retry/error_policy/prompt等）合并到 YAML StepDefinition 中，
    /// 生成可供非程序人员编辑的配置起点。
    /// YAML 已显式配置的值优先保留，仅填充 Handler 默认值中 YAML 未配置的部分。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="handlerDefaults">步骤ID → Handler 默认配置映射</param>
    /// <param name="version">版本号(可选,默认最新版本)</param>
    /// <returns>YAML格式的工作流定义（含 Handler 默认配置）</returns>
    public string ExportTemplate(string name, IReadOnlyDictionary<string, StepHandlerDefaults> handlerDefaults, string? version = null)
    {
        var definition = version != null
            ? _registry.GetByVersion(name, version)
            : _registry.Get(name);

        // 深拷贝: 反序列化再序列化以产生独立副本
        var yaml = _yamlSerializer.Serialize(definition);
        var clone = _yamlDeserializer.Deserialize<WorkflowDefinition>(yaml) ?? definition;

        // 清除拓扑字段 — 拓扑由代码定义，YAML 模板不参与覆盖
        foreach (var step in clone.Steps)
        {
            step.NextStepId = null;
            step.WaitMode = null;
            step.DependsOn = null;

            if (!handlerDefaults.TryGetValue(step.Id, out var defaults))
                continue;

            // 仅填充 YAML 未显式配置的字段（YAML 配置优先）
            if (string.IsNullOrWhiteSpace(step.Timeout))
                step.Timeout = defaults.Timeout;
            if (string.IsNullOrWhiteSpace(step.TimeoutAction))
                step.TimeoutAction = defaults.TimeoutAction;
            if (step.Retry == null && defaults.Retry != null)
                step.Retry = defaults.Retry;
            if (string.IsNullOrWhiteSpace(step.ErrorPolicy))
                step.ErrorPolicy = defaults.ErrorPolicy;
            if (string.IsNullOrWhiteSpace(step.Prompt))
                step.Prompt = defaults.Prompt;
            if (string.IsNullOrWhiteSpace(step.SystemPrompt))
                step.SystemPrompt = defaults.SystemPrompt;

            // Agent 专属字段
            if (step.Type == StepType.Agent)
            {
                if (string.IsNullOrWhiteSpace(step.RouteName))
                    step.RouteName = defaults.RouteName;
                if (string.IsNullOrWhiteSpace(step.EventType))
                    step.EventType = defaults.EventType;
            }

            // HumanApproval 专属字段
            if (step.Type == StepType.HumanApproval)
            {
                if (step.Notification == null && defaults.Notification != null)
                    step.Notification = defaults.Notification;
            }

            // 所有步骤类型通用
            if (string.IsNullOrWhiteSpace(step.HeartbeatExtension))
                step.HeartbeatExtension = defaults.HeartbeatExtension;
        }

        return _yamlSerializer.Serialize(clone);
    }

    /// <summary>
    /// 将工作流定义导出到YAML文件。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="filePath">输出文件路径</param>
    /// <param name="version">版本号(可选)</param>
    public async Task ExportToYamlFileAsync(string name, string filePath, string? version = null)
    {
        var yaml = ExportToYaml(name, version);
        await File.WriteAllTextAsync(filePath, yaml);
    }

    /// <summary>
    /// 从YAML字符串导入工作流定义。
    /// </summary>
    /// <param name="yamlContent">YAML内容</param>
    /// <param name="register">是否自动注册到注册表(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public WorkflowDefinition ImportFromYaml(string yamlContent, bool register = true)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            throw new ArgumentException("YAML内容不能为空", nameof(yamlContent));

        var definition = _yamlDeserializer.Deserialize<WorkflowDefinition>(yamlContent);

        if (definition == null)
            throw new InvalidOperationException("YAML解析结果为空");

        // 验证工作流定义
        var errors = definition.Validate();
        if (errors.Any())
        {
            throw new ValidationException(
                $"工作流定义验证失败:\n{string.Join("\n", errors.Select(e => $"- {e}"))}");
        }

        // 自动注册
        if (register)
        {
            _registry.Register(definition);
        }

        return definition;
    }

    /// <summary>
    /// 从YAML文件导入工作流定义。
    /// </summary>
    /// <param name="filePath">YAML文件路径</param>
    /// <param name="register">是否自动注册(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public async Task<WorkflowDefinition> ImportFromYamlFileAsync(string filePath, bool register = true)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"YAML文件不存在: {filePath}");

        var yamlContent = await File.ReadAllTextAsync(filePath);
        return ImportFromYaml(yamlContent, register);
    }

    /// <summary>
    /// 将工作流定义导出为JSON字符串。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号(可选)</param>
    /// <param name="includeMetadata">是否包含版本元数据(默认false)</param>
    /// <returns>JSON格式的工作流定义</returns>
    public string ExportToJson(string name, string? version = null, bool includeMetadata = false)
    {
        var definition = version != null
            ? _registry.GetByVersion(name, version)
            : _registry.Get(name);

        return JsonSerializer.Serialize(definition, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// 将工作流定义导出到JSON文件。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="filePath">输出文件路径</param>
    /// <param name="version">版本号(可选)</param>
    /// <param name="includeMetadata">是否包含元数据</param>
    public async Task ExportToJsonFileAsync(string name, string filePath, string? version = null, bool includeMetadata = false)
    {
        var json = ExportToJson(name, version, includeMetadata);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// 从JSON字符串导入工作流定义。
    /// </summary>
    /// <param name="jsonContent">JSON内容</param>
    /// <param name="register">是否自动注册(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public WorkflowDefinition ImportFromJson(string jsonContent, bool register = true)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            throw new ArgumentException("JSON内容不能为空", nameof(jsonContent));

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(jsonContent, options);

        if (definition == null)
            throw new InvalidOperationException("JSON解析结果为空");

        // 验证工作流定义
        var errors = definition.Validate();
        if (errors.Any())
        {
            throw new ValidationException(
                $"工作流定义验证失败:\n{string.Join("\n", errors.Select(e => $"- {e}"))}");
        }

        // 自动注册
        if (register)
        {
            _registry.Register(definition);
        }

        return definition;
    }

    /// <summary>
    /// 从JSON文件导入工作流定义。
    /// </summary>
    /// <param name="filePath">JSON文件路径</param>
    /// <param name="register">是否自动注册(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public async Task<WorkflowDefinition> ImportFromJsonFileAsync(string filePath, bool register = true)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"JSON文件不存在: {filePath}");

        var jsonContent = await File.ReadAllTextAsync(filePath);
        return ImportFromJson(jsonContent, register);
    }

    /// <summary>
    /// 批量导出所有已注册的工作流。
    /// </summary>
    /// <param name="outputDirectory">输出目录</param>
    /// <param name="format">导出格式(yaml或json)</param>
    /// <param name="includeAllVersions">是否导出所有版本(默认只导出默认版本)</param>
    public async Task ExportAllWorkflowsAsync(string outputDirectory, string format = "yaml", bool includeAllVersions = false)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var workflowNames = _registry.GetRegisteredNames().ToList();

        foreach (var name in workflowNames)
        {
            try
            {
                // 只导出默认版本
                var fileName = $"{name}.{format}";
                var filePath = Path.Combine(outputDirectory, fileName);

                if (format.ToLowerInvariant() == "yaml")
                {
                    await ExportToYamlFileAsync(name, filePath);
                }
                else
                {
                    await ExportToJsonFileAsync(name, filePath);
                }
            }
            catch (Exception ex)
            {
                // 记录错误但继续处理其他工作流
                System.Diagnostics.Debug.WriteLine($"导出工作流 {name} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 批量导入目录中的所有工作流定义文件。
    /// </summary>
    /// <param name="inputDirectory">输入目录</param>
    /// <param name="register">是否自动注册(默认true)</param>
    /// <returns>成功导入的工作流数量</returns>
    public async Task<int> ImportAllWorkflowsAsync(string inputDirectory, bool register = true)
    {
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"目录不存在: {inputDirectory}");

        var files = Directory.GetFiles(inputDirectory, "*.*")
            .Where(f => f.EndsWith(".yaml") || f.EndsWith(".yml") || f.EndsWith(".json"))
            .ToList();

        int successCount = 0;

        foreach (var file in files)
        {
            try
            {
                if (file.EndsWith(".yaml") || file.EndsWith(".yml"))
                {
                    await ImportFromYamlFileAsync(file, register);
                }
                else if (file.EndsWith(".json"))
                {
                    await ImportFromJsonFileAsync(file, register);
                }

                successCount++;
            }
            catch (Exception ex)
            {
                // 记录错误但继续处理其他文件
                System.Diagnostics.Debug.WriteLine($"导入文件 {file} 失败: {ex.Message}");
            }
        }

        return successCount;
    }

    /// <summary>
    /// 创建工作流定义的备份归档。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="backupDirectory">备份目录</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> CreateBackupAsync(string name, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{name}_backup_{timestamp}.yaml";
        var filePath = Path.Combine(backupDirectory, fileName);

        await ExportToYamlFileAsync(name, filePath);

        return filePath;
    }

    /// <summary>
    /// 生成工作流的摘要信息。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号(可选)</param>
    /// <returns>工作流摘要信息</returns>
    public WorkflowSummary GenerateSummary(string name, string? version = null)
    {
        var definition = version != null
            ? _registry.GetByVersion(name, version)
            : _registry.Get(name);

        var summary = new WorkflowSummary
        {
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            TotalSteps = CountSteps(definition.Steps),
            AgentSteps = definition.Steps.Count(s => s.Type == StepType.Agent),
            CodeSteps = definition.Steps.Count(s => s.Type == StepType.Code),
            DelaySteps = definition.Steps.Count(s => s.Type == StepType.Delay),
            HumanApprovalSteps = definition.Steps.Count(s => s.Type == StepType.HumanApproval),
            SubWorkflowSteps = definition.Steps.Count(s => s.Type == StepType.Workflow),
            CreatedAt = DateTime.UtcNow,
            CurrentVersion = definition.Version,
            Versions = _registry.GetVersions(name).ToList()
        };

        return summary;
    }

    private static int CountSteps(List<StepDefinition> steps)
    {
        int count = 0;
        foreach (var step in steps)
        {
            count++;
            if (step.Steps != null && step.Steps.Any())
            {
                count += CountSteps(step.Steps);
            }
        }
        return count;
    }
}

/// <summary>
/// 工作流摘要信息。
/// </summary>
public class WorkflowSummary
{
    /// <summary>工作流名称</summary>
    public string Name { get; set; } = "";

    /// <summary>版本号</summary>
    public string Version { get; set; } = "";

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>总步骤数</summary>
    public int TotalSteps { get; set; }

    /// <summary>Agent步骤数</summary>
    public int AgentSteps { get; set; }

    /// <summary>代码步骤数</summary>
    public int CodeSteps { get; set; }

    /// <summary>延迟步骤数</summary>
    public int DelaySteps { get; set; }

    /// <summary>人工审批步骤数</summary>
    public int HumanApprovalSteps { get; set; }

    /// <summary>子工作流步骤数</summary>
    public int SubWorkflowSteps { get; set; }

    /// <summary>所有版本列表</summary>
    public List<string> Versions { get; set; } = new();

    /// <summary>当前默认版本</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>生成时间</summary>
    public DateTime CreatedAt { get; set; }
}

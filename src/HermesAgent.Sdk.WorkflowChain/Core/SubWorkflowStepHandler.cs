namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 子工作流步骤处理器 — 在当前工作流中嵌套执行另一个工作流。
/// 支持输入映射和输出映射,实现父子工作流之间的数据传递。
/// </summary>
public abstract class SubWorkflowStepHandler : StepHandlerBase
{
    /// <summary>子工作流名称</summary>
    public abstract string SubWorkflowName { get; }

    /// <summary>子工作流版本(可选,默认最新版本)</summary>
    public virtual string? SubWorkflowVersion => null;

    /// <summary>
    /// 输入映射配置:父工作流上下文 → 子工作流输入
    /// Key: 子工作流输入参数名
    /// Value: 父工作流中的变量路径(如 "data.userId" 或 "stepOutputs.review.result")
    /// </summary>
    public virtual Dictionary<string, string> InputMapping { get; } = new();

    /// <summary>
    /// 输出映射配置:子工作流输出 → 父工作流上下文
    /// Key: 父工作流中存储的键名
    /// Value: 子工作流输出中的字段路径
    /// </summary>
    public virtual Dictionary<string, string> OutputMapping { get; } = new();

    /// <summary>
    /// 构建子工作流的输入参数。
    /// 默认实现根据 InputMapping 从父上下文提取数据。
    /// </summary>
    public virtual Dictionary<string, object?> BuildSubWorkflowInput(WorkflowContext parentContext)
    {
        var input = new Dictionary<string, object?>();

        foreach (var mapping in InputMapping)
        {
            var childParamName = mapping.Key;
            var parentPath = mapping.Value;

            var value = ResolvePath(parentContext, parentPath);
            input[childParamName] = value;
        }

        return input;
    }

    /// <summary>
    /// 处理子工作流的输出,映射回父工作流上下文。
    /// 默认实现根据 OutputMapping 将子工作流输出写入父上下文。
    /// </summary>
    public virtual void MapSubWorkflowOutput(
        WorkflowContext parentContext,
        Dictionary<string, object?> subWorkflowOutput)
    {
        foreach (var mapping in OutputMapping)
        {
            var parentKey = mapping.Key;
            var childOutputPath = mapping.Value;

            var value = ResolveDictionaryPath(subWorkflowOutput, childOutputPath);
            parentContext.SetData(parentKey, value);
        }
    }

    /// <summary>
    /// 解析路径表达式,从上下文中提取值。
    /// 支持格式:
    /// - "data.key" → context.Data["key"]
    /// - "stepOutputs.stepId" → context.StepOutputs["stepId"]
    /// - "input.paramName" → context.InitialInput["paramName"]
    /// - "literal:value" → 直接返回值 "value"
    /// </summary>
    protected virtual object? ResolvePath(WorkflowContext context, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // 字面量值
        if (path.StartsWith("literal:", StringComparison.OrdinalIgnoreCase))
            return path.Substring("literal:".Length);

        var parts = path.Split(new[] { '.' }, 2);
        if (parts.Length != 2)
            return null;

        var source = parts[0].ToLowerInvariant();
        var key = parts[1];

        return source switch
        {
            "data" => context.Data.TryGetValue(key, out var val) ? val : null,
            "stepoutputs" or "output" => context.StepOutputs.TryGetValue(key, out var val) ? val : null,
            "input" => context.InitialInput.TryGetValue(key, out var val) ? val : null,
            _ => null
        };
    }

    /// <summary>
    /// 从字典中解析嵌套路径。
    /// 支持格式: "result.approved" → dict["result"]["approved"]
    /// </summary>
    protected virtual object? ResolveDictionaryPath(Dictionary<string, object?> dict, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var keys = path.Split('.');
        object? current = dict;

        foreach (var key in keys)
        {
            if (current is not Dictionary<string, object?> currentDict)
                return null;

            if (!currentDict.TryGetValue(key, out var value))
                return null;

            current = value;
        }

        return current;
    }
}

/// <summary>
/// 基于配置的子工作流步骤处理器。
/// 适用于从 YAML 定义动态创建的子工作流步骤。
/// </summary>
public class ConfigurableSubWorkflowStepHandler : SubWorkflowStepHandler
{
    private readonly string _stepId;
    private readonly string _subWorkflowName;
    private readonly string? _subWorkflowVersion;

    public override string StepId => _stepId;
    public override string SubWorkflowName => _subWorkflowName;
    public override string? SubWorkflowVersion => _subWorkflowVersion;
    public override Dictionary<string, string> InputMapping { get; }
    public override Dictionary<string, string> OutputMapping { get; }

    public ConfigurableSubWorkflowStepHandler(
        string stepId,
        string subWorkflowName,
        string? subWorkflowVersion = null,
        Dictionary<string, string>? inputMapping = null,
        Dictionary<string, string>? outputMapping = null)
    {
        _stepId = stepId;
        _subWorkflowName = subWorkflowName;
        _subWorkflowVersion = subWorkflowVersion;
        InputMapping = inputMapping ?? new Dictionary<string, string>();
        OutputMapping = outputMapping ?? new Dictionary<string, string>();
    }

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        // 子工作流的实际执行由 WorkflowEngine 处理
        // 这里返回一个特殊标记结果，Engine 通过类型判断识别为子工作流并启动执行
        return Task.FromResult(new StepResult
        {
            StepId = StepId,
            IsSuccess = true,
            // Engine 在 ExecuteStepAsync 中通过 is SubWorkflowStepHandler 判断跳过此调用
            // 因此这个返回值实际上不会被使用，但避免抛出异常
        });
    }
}

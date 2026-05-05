using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 变量替换引擎 - 解析YAML模板中的变量表达式。
/// 支持的语法:
/// - {{steps.step_id.output}} - 引用前序步骤输出
/// - {{steps.step_id.output.property.path}} - 引用嵌套属性
/// - {{context.data_key}} - 引用上下文数据
/// </summary>
public class VariableResolver
{
    private readonly WorkflowContext _context;
    private readonly ILogger? _logger;
    private static readonly Regex VariablePattern = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// 创建变量解析器实例。
    /// </summary>
    /// <param name="context">工作流上下文</param>
    /// <param name="logger">日志记录器（可选，用于记录属性缺失等诊断信息）</param>
    public VariableResolver(WorkflowContext context, ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    /// <summary>
    /// 解析模板字符串中的所有变量表达式。
    /// </summary>
    /// <param name="template">包含变量表达式的模板字符串</param>
    /// <returns>替换后的字符串</returns>
    public string Resolve(string template)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return VariablePattern.Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim();
            return ResolveExpression(expression);
        });
    }

    private string ResolveExpression(string expression)
    {
        // steps.xxx.output.yyy
        if (expression.StartsWith("steps.", StringComparison.OrdinalIgnoreCase))
            return ResolveStepOutput(expression);

        // context.xxx
        if (expression.StartsWith("context.", StringComparison.OrdinalIgnoreCase))
            return ResolveContextData(expression);

        throw new InvalidOperationException($"未知的变量表达式: {expression}");
    }

    private string ResolveStepOutput(string expression)
    {
        // 解析: steps.step_id.output.property.path
        var parts = expression.Split('.');
        if (parts.Length < 3)
            throw new InvalidOperationException($"无效的steps表达式: {expression}, 格式应为: steps.step_id.output[.property]");

        var stepId = parts[1];
        var propertyPath = string.Join(".", parts.Skip(3)); // 跳过 "steps", stepId, "output"

        if (!_context.StepOutputs.TryGetValue(stepId, out var output))
            throw new InvalidOperationException($"步骤 {stepId} 的输出不存在");

        if (string.IsNullOrEmpty(propertyPath))
            return SerializeValue(output);

        return ExtractProperty(output, propertyPath);
    }

    private string ResolveContextData(string expression)
    {
        // 解析: context.data_key 或 context.data_key.nested.property
        var parts = expression.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException($"无效的context表达式: {expression}, 格式应为: context.key[.nested]");

        var key = parts[1];
        var propertyPath = string.Join(".", parts.Skip(2));

        if (!_context.Data.TryGetValue(key, out var value))
            throw new InvalidOperationException($"上下文数据 {key} 不存在");

        if (string.IsNullOrEmpty(propertyPath))
            return SerializeValue(value);

        return ExtractProperty(value, propertyPath);
    }

    private string ExtractProperty(object? obj, string propertyPath)
    {
        if (obj == null)
            return "null";

        var properties = propertyPath.Split('.');
        var current = obj;

        foreach (var prop in properties)
        {
            if (current == null)
                return "null";

            // 尝试字典访问
            if (current is Dictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(prop, out current))
                {
                    _logger?.LogDebug("属性路径 '{PropertyPath}' 中字典键 '{Key}' 不存在，返回 null", propertyPath, prop);
                    return "null";
                }
            }
            // 尝试反射访问
            else
            {
                var type = current.GetType();
                var propertyInfo = type.GetProperty(prop);
                if (propertyInfo == null)
                {
                    _logger?.LogDebug("属性路径 '{PropertyPath}' 中类型 {Type} 不存在属性 '{Prop}'，返回 null", propertyPath, type.Name, prop);
                    return "null";
                }

                current = propertyInfo.GetValue(current);
            }
        }

        return SerializeValue(current);
    }

    private string SerializeValue(object? value)
    {
        if (value == null)
            return "null";

        if (value is string str)
            return str;

        if (value is int || value is long || value is float || value is double || value is decimal || value is bool)
            return value.ToString()!;

        // 复杂对象序列化为JSON — 加 try-catch 防止循环引用导致整个模板替换失败
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            return $"<unserializable:{value.GetType().Name}>";
        }
    }
}

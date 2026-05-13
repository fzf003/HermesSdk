using YamlDotNet.Serialization;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// YAML工作流解析器 - 将YAML文件解析为WorkflowDefinition。
/// </summary>
public class YamlWorkflowParser
{
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// 创建YAML解析器实例。
    /// </summary>
    public YamlWorkflowParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithTypeConverter(new StepTypeConverter())
            .WithTypeConverter(new RetryPolicyConverter())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// 从YAML字符串解析工作流定义。
    /// </summary>
    /// <param name="yamlContent">YAML内容</param>
    /// <param name="validate">是否执行验证(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    /// <exception cref="ValidationException">验证失败时抛出</exception>
    public WorkflowDefinition Parse(string yamlContent, bool validate = true)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            throw new ArgumentException("YAML内容不能为空", nameof(yamlContent));

        try
        {
            var definition = _deserializer.Deserialize<WorkflowDefinition>(yamlContent);

            if (definition == null)
                throw new InvalidOperationException("YAML解析结果为null");

            // 执行验证
            if (validate)
            {
                var errors = definition.Validate();
                if (errors.Any())
                {
                    throw new ValidationException(
                        $"工作流定义验证失败:\n{string.Join("\n", errors.Select(e => $"- {e}"))}");
                }
            }

            return definition;
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new InvalidOperationException($"YAML解析失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从YAML文件解析工作流定义（同步方法）。
    /// </summary>
    /// <param name="filePath">YAML文件路径</param>
    /// <param name="validate">是否执行验证(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public WorkflowDefinition ParseFromFile(string filePath, bool validate = true)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"YAML文件不存在: {filePath}");

        var yamlContent = File.ReadAllText(filePath);
        return Parse(yamlContent, validate);
    }

    /// <summary>
    /// 从YAML文件解析工作流定义（异步方法）。
    /// </summary>
    /// <param name="filePath">YAML文件路径</param>
    /// <param name="validate">是否执行验证(默认true)</param>
    /// <returns>解析后的工作流定义</returns>
    public async Task<WorkflowDefinition> ParseFromFileAsync(string filePath, bool validate = true)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"YAML文件不存在: {filePath}");

        var yamlContent = await File.ReadAllTextAsync(filePath);
        return Parse(yamlContent, validate);
    }
}

/// <summary>
/// 工作流定义验证异常。
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// 创建验证异常。
    /// </summary>
    /// <param name="message">错误消息</param>
    public ValidationException(string message) : base(message)
    {
    }

    /// <summary>
    /// 创建验证异常。
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

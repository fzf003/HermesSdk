namespace HermesAgent.Sdk.WorkflowChain;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

public enum StepType
{
    Agent, Code, Delay, HumanApproval, Workflow, Sequential, Parallel,
}

public class StepTypeConverter : IYamlTypeConverter
{
    private static readonly Dictionary<string, StepType> StringToEnum = new()
    {
        ["agent"] = StepType.Agent, ["code"] = StepType.Code, ["delay"] = StepType.Delay,
        ["human-approval"] = StepType.HumanApproval, ["workflow"] = StepType.Workflow,
        ["sequential"] = StepType.Sequential, ["parallel"] = StepType.Parallel,
    };
    private static readonly Dictionary<StepType, string> EnumToString = StringToEnum.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
    public bool Accepts(Type type) => type == typeof(StepType);
    public object? ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("StepType不能为空");
        if (StringToEnum.TryGetValue(value.ToLower(), out var stepType)) return stepType;
        throw new InvalidOperationException($"未知的步骤类型: {value}");
    }
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        if (value == null) { emitter.Emit(new Scalar(null!)); return; }
        var stepType = (StepType)value;
        var yamlValue = EnumToString.TryGetValue(stepType, out var str) ? str : stepType.ToString();
        emitter.Emit(new Scalar(yamlValue));
    }
}

public class StepDefinition
{
    [YamlMember(Alias = "id")] public string Id { get; set; } = "";
    [YamlMember(Alias = "type")] public StepType Type { get; set; }
    [YamlMember(Alias = "assembly")] public string? Assembly { get; set; }
    [YamlMember(Alias = "class")] public string? Class { get; set; }
    [YamlMember(Alias = "model")] public string? Model { get; set; }
    [YamlMember(Alias = "prompt")] public string? Prompt { get; set; }
    [YamlMember(Alias = "system_prompt")] public string? SystemPrompt { get; set; }
    [YamlMember(Alias = "communication_mode")] public string? CommunicationMode { get; set; }
    [YamlMember(Alias = "route_name")] public string? RouteName { get; set; }
    [YamlMember(Alias = "event_type")] public string? EventType { get; set; }
    [YamlMember(Alias = "notification")] public ApprovalNotificationConfig? Notification { get; set; }
    [YamlMember(Alias = "heartbeat_extension")] public string? HeartbeatExtension { get; set; }
    [YamlMember(Alias = "workflow_name")] public string? WorkflowName { get; set; }
    [YamlMember(Alias = "workflow_version")] public string? WorkflowVersion { get; set; }
    
    // 主属性：支持 'input_mapping' 字段
    [YamlMember(Alias = "input_mapping")] 
    public Dictionary<string, string>? InputMapping { get; set; }
    
    // 向后兼容：支持 'input' 字段作为别名（通过自定义解析器处理）
    private Dictionary<string, string>? _inputAlias;
    [YamlMember(Alias = "input")]
    public Dictionary<string, string>? InputAlias 
    { 
        get => _inputAlias; 
        set 
        { 
            _inputAlias = value;
            // 如果 InputMapping 为空，则使用 InputAlias 的值
            if (value != null && InputMapping == null)
                InputMapping = value;
        }
    }
    [YamlMember(Alias = "duration")] public string? Duration { get; set; }
    [YamlMember(Alias = "next_step_id")] public string? NextStepId { get; set; }
    [YamlMember(Alias = "wait_mode")] public string? WaitMode { get; set; }
    [YamlMember(Alias = "steps")] public List<StepDefinition>? Steps { get; set; }
    [YamlMember(Alias = "timeout")] public string? Timeout { get; set; }
    [YamlMember(Alias = "timeout_action")] public string? TimeoutAction { get; set; }
    [YamlMember(Alias = "retry")] public RetryConfigYaml? Retry { get; set; }
    [YamlMember(Alias = "error_policy")] public string? ErrorPolicy { get; set; }
    [YamlMember(Alias = "depends_on")] public List<string>? DependsOn { get; set; }
}

public class ApprovalNotificationConfig
{
    [YamlMember(Alias = "email")] public string? Email { get; set; }
    [YamlMember(Alias = "im")] public string? Im { get; set; }
    [YamlMember(Alias = "push")] public string? Push { get; set; }
    [YamlMember(Alias = "message")] public string? Message { get; set; }
}

public class RetryConfigYaml
{
    [YamlMember(Alias = "max_retries")] public int MaxRetries { get; set; } = 3;
    [YamlMember(Alias = "policy")] public string? Policy { get; set; }
    [YamlMember(Alias = "initial_delay")] public string? InitialDelay { get; set; }
    [YamlMember(Alias = "backoff_factor")] public double BackoffFactor { get; set; } = 2.0;
    [YamlMember(Alias = "max_delay")] public string? MaxDelay { get; set; }
}

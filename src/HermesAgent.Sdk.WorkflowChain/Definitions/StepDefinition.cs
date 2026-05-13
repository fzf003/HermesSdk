namespace HermesAgent.Sdk.WorkflowChain;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>步骤类型，决定步骤的执行方式和所需字段。</summary>
public enum StepType
{
    /// <summary>Agent 步骤 —— 调用 LLM 模型。需指定 model、prompt。</summary>
    Agent,
    /// <summary>代码步骤 —— 执行 .NET 处理器类。需指定 assembly、class。</summary>
    Code,
    /// <summary>延迟步骤 —— 等待指定时长后继续。需指定 duration、next_step_id。</summary>
    Delay,
    /// <summary>人工审批步骤 —— 等待人工审批结果。需指定 notification。</summary>
    HumanApproval,
    /// <summary>子工作流步骤 —— 嵌套执行另一个工作流。需指定 workflow_name。</summary>
    Workflow,
    /// <summary>顺序容器 —— 按顺序执行一组子步骤。</summary>
    Sequential,
    /// <summary>并行容器 —— 并行执行一组子步骤。</summary>
    Parallel,
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

/// <summary>
/// RetryPolicy 的 YAML 类型转换器。
/// 将 "immediate" / "exponential_backoff" / "fixed_interval" / "custom" 与 RetryPolicy 枚举互转。
/// </summary>
public class RetryPolicyConverter : IYamlTypeConverter
{
    private static readonly Dictionary<string, RetryPolicy> StringToEnum = new(StringComparer.OrdinalIgnoreCase)
    {
        ["immediate"] = RetryPolicy.Immediate,
        ["exponential_backoff"] = RetryPolicy.ExponentialBackoff,
        ["exponential"] = RetryPolicy.ExponentialBackoff,
        ["fixed"] = RetryPolicy.FixedInterval,
        ["fixed_interval"] = RetryPolicy.FixedInterval,
        ["custom"] = RetryPolicy.Custom,
    };

    public bool Accepts(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        return targetType == typeof(RetryPolicy);
    }

    public object? ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        if (string.IsNullOrEmpty(value)) return null;
        if (StringToEnum.TryGetValue(value, out var policy)) return policy;
        return RetryPolicy.ExponentialBackoff;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        if (value is not RetryPolicy policy)
        {
            emitter.Emit(new Scalar("exponential_backoff"));
            return;
        }
        var str = policy switch
        {
            RetryPolicy.Immediate => "immediate",
            RetryPolicy.ExponentialBackoff => "exponential_backoff",
            RetryPolicy.FixedInterval => "fixed_interval",
            RetryPolicy.Custom => "custom",
            _ => "exponential_backoff"
        };
        emitter.Emit(new Scalar(str));
    }
}

/// <summary>
/// 策略感知的 RetryConfig 序列化器。
/// 只输出与当前 Policy 关联的参数，无关字段不序列化。
/// </summary>
public class RetryConfigConverter : IYamlTypeConverter
{
    private static readonly Dictionary<string, RetryPolicy> StringToPolicy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["immediate"] = RetryPolicy.Immediate,
        ["exponential_backoff"] = RetryPolicy.ExponentialBackoff,
        ["exponential"] = RetryPolicy.ExponentialBackoff,
        ["fixed"] = RetryPolicy.FixedInterval,
        ["fixed_interval"] = RetryPolicy.FixedInterval,
    };

    public bool Accepts(Type type) => type == typeof(RetryConfigYaml);

    public object? ReadYaml(IParser parser, Type type)
    {
        if (parser.Accept<Scalar>(out var nullScalar) && nullScalar.Value == null)
        {
            parser.Consume<Scalar>();
            return null;
        }

        parser.Consume<MappingStart>();
        var config = new RetryConfigYaml();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            switch (key)
            {
                case "max_retries":
                    config.MaxRetries = int.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "policy":
                    var policyStr = parser.Consume<Scalar>().Value;
                    if (!string.IsNullOrEmpty(policyStr) && StringToPolicy.TryGetValue(policyStr, out var p))
                        config.Policy = p;
                    break;
                case "initial_delay":
                    config.InitialDelay = parser.Consume<Scalar>().Value;
                    break;
                case "backoff_factor":
                    if (double.TryParse(parser.Consume<Scalar>().Value, out var bf))
                        config.BackoffFactor = bf;
                    break;
                case "max_delay":
                    config.MaxDelay = parser.Consume<Scalar>().Value;
                    break;
                default:
                    parser.SkipThisAndNestedEvents();
                    break;
            }
        }

        return config;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        if (value is not RetryConfigYaml retry)
        {
            emitter.Emit(new Scalar("null"));
            return;
        }

        emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));

        // max_retries 始终输出
        emitter.Emit(new Scalar("max_retries"));
        emitter.Emit(new Scalar(retry.MaxRetries.ToString()));

        // policy 始终输出
        var policyStr = retry.Policy switch
        {
            RetryPolicy.Immediate => "immediate",
            RetryPolicy.ExponentialBackoff => "exponential_backoff",
            RetryPolicy.FixedInterval => "fixed_interval",
            null => null,
            _ => null
        };
        if (policyStr != null)
        {
            emitter.Emit(new Scalar("policy"));
            emitter.Emit(new Scalar(policyStr));
        }

        // 按策略输出关联参数
        switch (retry.Policy)
        {
            case RetryPolicy.ExponentialBackoff:
                if (!string.IsNullOrEmpty(retry.InitialDelay))
                {
                    emitter.Emit(new Scalar("initial_delay"));
                    emitter.Emit(new Scalar(retry.InitialDelay));
                }
                if (retry.BackoffFactor.HasValue && retry.BackoffFactor.Value >= 0)
                {
                    emitter.Emit(new Scalar("backoff_factor"));
                    emitter.Emit(new Scalar(retry.BackoffFactor.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
                }
                if (!string.IsNullOrEmpty(retry.MaxDelay))
                {
                    emitter.Emit(new Scalar("max_delay"));
                    emitter.Emit(new Scalar(retry.MaxDelay));
                }
                break;

            case RetryPolicy.FixedInterval:
                if (!string.IsNullOrEmpty(retry.InitialDelay))
                {
                    emitter.Emit(new Scalar("initial_delay"));
                    emitter.Emit(new Scalar(retry.InitialDelay));
                }
                if (!string.IsNullOrEmpty(retry.MaxDelay))
                {
                    emitter.Emit(new Scalar("max_delay"));
                    emitter.Emit(new Scalar(retry.MaxDelay));
                }
                break;

            // Immediate / null: 不输出额外参数
        }

        emitter.Emit(new MappingEnd());
    }
}

/// <summary>步骤定义 —— 描述工作流中一个步骤的完整配置。</summary>
/// <remarks>
/// 运行时配置字段（timeout / timeout_action / retry / error_policy / prompt / system_prompt）
/// 具有合并优先级：<c>YAML 值 &gt; Handler 虚属性默认值 &gt; 引擎内建默认值</c>。
/// 拓扑字段（depends_on / next_step_id / steps / wait_mode）不在合并范围内，
/// 仅由 YAML 或代码定义决定。
/// </remarks>
public class StepDefinition
{
    /// <summary>步骤标识符，在同一工作流内唯一。</summary>
    [YamlMember(Alias = "id")] public string Id { get; set; } = "";

    /// <summary>步骤类型：code / agent / delay / human-approval / workflow / sequential / parallel。</summary>
    [YamlMember(Alias = "type")] public StepType Type { get; set; }

    /// <summary>Code 步骤 —— 处理器所在的程序集名称。</summary>
    [YamlMember(Alias = "assembly")] public string? Assembly { get; set; }

    /// <summary>Code 步骤 —— 处理器的完整类名（不带命名空间时自动搜索程序集）。</summary>
    [YamlMember(Alias = "class")] public string? Class { get; set; }

    /// <summary>Agent 步骤 —— 调用的 LLM 模型标识。</summary>
    [YamlMember(Alias = "model")] public string? Model { get; set; }

    /// <summary>Agent 步骤 —— 用户提示词。合并优先级：YAML prompt &gt; Handler.Prompt 虚属性 &gt; BuildPrompt() 回退。</summary>
    [YamlMember(Alias = "prompt")] public string? Prompt { get; set; }

    /// <summary>Agent 步骤 —— 系统提示词。合并优先级：YAML system_prompt &gt; Handler.SystemPrompt 虚属性。</summary>
    [YamlMember(Alias = "system_prompt")] public string? SystemPrompt { get; set; }

    /// <summary>Agent 步骤 —— 通信模式：webhook / run_client。覆盖 Handler.Mode 虚属性。</summary>
    [YamlMember(Alias = "communication_mode")] public string? CommunicationMode { get; set; }

    /// <summary>Agent 步骤（Webhook 模式） —— Webhook 路由名称。</summary>
    [YamlMember(Alias = "route_name")] public string? RouteName { get; set; }

    /// <summary>Agent 步骤（Webhook 模式） —— Webhook 事件类型。</summary>
    [YamlMember(Alias = "event_type")] public string? EventType { get; set; }

    /// <summary>HumanApproval 步骤 —— 审批通知配置（邮箱 / IM / 推送）。</summary>
    [YamlMember(Alias = "notification")] public ApprovalNotificationConfig? Notification { get; set; }

    /// <summary>HumanApproval 步骤 —— 心跳延长时间，引擎在此时间内认为步骤仍存活。</summary>
    [YamlMember(Alias = "heartbeat_extension")] public string? HeartbeatExtension { get; set; }

    /// <summary>Workflow（子工作流）步骤 —— 要执行的工作流名称。</summary>
    [YamlMember(Alias = "workflow_name")] public string? WorkflowName { get; set; }

    /// <summary>Workflow（子工作流）步骤 —— 要执行的工作流版本。</summary>
    [YamlMember(Alias = "workflow_version")] public string? WorkflowVersion { get; set; }

    /// <summary>输入字段映射 —— 将上下文数据按 key 映射到步骤的输入变量。</summary>
    [YamlMember(Alias = "input_mapping")]
    public Dictionary<string, string>? InputMapping { get; set; }

    // 向后兼容：支持 'input' 字段作为 input_mapping 的别名
    private Dictionary<string, string>? _inputAlias;
    /// <summary>输入映射别名（"input"），向后兼容。赋值时会同步到 <see cref="InputMapping"/>。</summary>
    [YamlMember(Alias = "input")]
    public Dictionary<string, string>? InputAlias
    {
        get => _inputAlias;
        set
        {
            _inputAlias = value;
            if (value != null && InputMapping == null)
                InputMapping = value;
        }
    }

    /// <summary>Delay 步骤 —— 延迟时长（如 "5s", "00:01:00"）。</summary>
    [YamlMember(Alias = "duration")] public string? Duration { get; set; }

    /// <summary>Delay 步骤 —— 延迟结束后执行的下一步骤 ID。</summary>
    [YamlMember(Alias = "next_step_id")] public string? NextStepId { get; set; }

    /// <summary>并行等待模式：all（等待全部完成）/ any（任一完成即继续）。</summary>
    [YamlMember(Alias = "wait_mode")] public string? WaitMode { get; set; }

    /// <summary>子步骤集合，仅用于 Sequential / Parallel 容器类型。</summary>
    [YamlMember(Alias = "steps")] public List<StepDefinition>? Steps { get; set; }

    /// <summary>步骤超时时间。合并优先级：YAML timeout &gt; Handler.Timeout 虚属性。</summary>
    [YamlMember(Alias = "timeout")] public string? Timeout { get; set; }

    /// <summary>超时后的处理动作：fail（默认）/ skip。合并优先级：YAML &gt; Handler 虚属性。</summary>
    [YamlMember(Alias = "timeout_action")] public string? TimeoutAction { get; set; }

    /// <summary>失败重试策略。合并优先级：YAML retry &gt; Handler.Retry 虚属性。</summary>
    [YamlMember(Alias = "retry")] public RetryConfigYaml? Retry { get; set; }

    /// <summary>错误策略：skip_failed_branch（跳过失败分支）。合并优先级：YAML &gt; Handler 虚属性。</summary>
    [YamlMember(Alias = "error_policy")] public string? ErrorPolicy { get; set; }

    /// <summary>前置依赖步骤 ID 列表。当前步骤等待所有依赖步骤完成后才执行。</summary>
    [YamlMember(Alias = "depends_on")] public List<string>? DependsOn { get; set; }
}

/// <summary>HumanApproval 步骤的审批通知配置。</summary>
public class ApprovalNotificationConfig
{
    /// <summary>通知邮箱地址。</summary>
    [YamlMember(Alias = "email")] public string? Email { get; set; }

    /// <summary>即时通讯通知目标。</summary>
    [YamlMember(Alias = "im")] public string? Im { get; set; }

    /// <summary>推送通知目标。</summary>
    [YamlMember(Alias = "push")] public string? Push { get; set; }

    /// <summary>通知消息正文。</summary>
    [YamlMember(Alias = "message")] public string? Message { get; set; }
}

/// <summary>重试策略配置。合并优先级：YAML retry &gt; Handler.Retry 虚属性。</summary>
/// <remarks>
/// Policy 说明：
/// <list type="bullet">
///   <item><term>immediate</term><description>失败后立即重试</description></item>
///   <item><term>exponential_backoff</term><description>指数退避：延迟 = initial_delay × backoff_factor^(attempt-1)，上限 max_delay</description></item>
/// </list>
/// </remarks>
public class RetryConfigYaml
{
    /// <summary>最大重试次数（不含首次执行）。默认值：3。</summary>
    [YamlMember(Alias = "max_retries")] public int MaxRetries { get; set; } = 3;

    /// <summary>重试策略：immediate / exponential_backoff。</summary>
    [YamlMember(Alias = "policy")] public RetryPolicy? Policy { get; set; }

    /// <summary>首次重试的初始延迟时间（如 "1s", "00:00:01"）。仅 policy=exponential_backoff 时生效。</summary>
    [YamlMember(Alias = "initial_delay")] public string? InitialDelay { get; set; }

    /// <summary>指数退避倍数，每次重试延迟乘以该值。默认值：2.0。未设置时不序列化。</summary>
    [YamlMember(Alias = "backoff_factor")] public double? BackoffFactor { get; set; }

    /// <summary>最大延迟上限（如 "30s"），防止退避无限增长。</summary>
    [YamlMember(Alias = "max_delay")] public string? MaxDelay { get; set; }
}

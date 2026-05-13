namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// AgentStepHandler 的 Fluent 配置子构建器。
/// 继承 CodeStepBuilder 共享通用步骤策略配置，并增加 prompt 专属配置。
/// 由 WorkflowChainBuilder.AddAgentStep 内部使用，不直接实例化。
/// </summary>
public sealed class AgentStepBuilder<T> where T : AgentStepHandler
{
    private string? _timeout;
    private string? _timeoutAction;
    private RetryConfigYaml? _retry;
    private string? _errorPolicy;
    private string? _prompt;
    private string? _systemPrompt;
    private string? _routeName;
    private string? _eventType;
    private string? _heartbeatExtension;

    /// <summary>设置超时时间</summary>
    public AgentStepBuilder<T> WithTimeout(string timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>设置超时行为</summary>
    public AgentStepBuilder<T> WithTimeoutAction(TimeoutAction action)
    {
        _timeoutAction = action switch
        {
            TimeoutAction.Throw => "throw",
            TimeoutAction.Fail => "fail",
            TimeoutAction.Skip => "skip",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return this;
    }

    /// <summary>配置重试策略（按策略类型只暴露关联参数）</summary>
    public AgentStepBuilder<T> WithRetry(Action<RetryConfigBuilder> configure)
    {
        if (_retry is not null)
            throw new InvalidOperationException("WithRetry 已被调用过，每个步骤只允许配置一次重试策略。");
        var builder = new RetryConfigBuilder();
        configure(builder);
        _retry = builder.Build();
        return this;
    }

    /// <summary>设置错误处理策略</summary>
    public AgentStepBuilder<T> WithErrorPolicy(ErrorPolicy policy)
    {
        _errorPolicy = policy switch
        {
            ErrorPolicy.FailFast => "fail_fast",
            ErrorPolicy.ContinueOnError => "continue_on_error",
            ErrorPolicy.SkipFailedBranch => "skip_failed_branch",
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        return this;
    }

    /// <summary>设置 Agent prompt 模板</summary>
    public AgentStepBuilder<T> WithPrompt(string prompt)
    {
        _prompt = prompt;
        return this;
    }

    /// <summary>设置 Agent system_prompt 模板</summary>
    public AgentStepBuilder<T> WithSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
        return this;
    }

    /// <summary>设置 Webhook 路由名称（覆盖 Handler.RouteName 虚属性）</summary>
    public AgentStepBuilder<T> WithRouteName(string routeName)
    {
        _routeName = routeName;
        return this;
    }

    /// <summary>设置 Webhook 事件类型（覆盖 Handler.EventType 虚属性）</summary>
    public AgentStepBuilder<T> WithEventType(string eventType)
    {
        _eventType = eventType;
        return this;
    }

    /// <summary>设置心跳扩展时长（覆盖 Handler.HeartbeatExtension 虚属性）</summary>
    public AgentStepBuilder<T> WithHeartbeatExtension(string heartbeatExtension)
    {
        _heartbeatExtension = heartbeatExtension;
        return this;
    }

    /// <summary>产出 StepHandlerDefaults 供内部字典存储</summary>
    internal StepHandlerDefaults Build() => new()
    {
        Timeout = _timeout,
        TimeoutAction = _timeoutAction,
        Retry = _retry,
        ErrorPolicy = _errorPolicy,
        Prompt = _prompt,
        SystemPrompt = _systemPrompt,
        RouteName = _routeName,
        EventType = _eventType,
        HeartbeatExtension = _heartbeatExtension,
    };
}

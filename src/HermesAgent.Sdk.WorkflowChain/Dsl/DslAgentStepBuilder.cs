namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// Agent 步骤的 Fluent 配置构建器。由 <c>AddAgentStep(id, cfg)</c> 返回。
/// 继承 <see cref="DslCodeStepBuilder"/> 共享通用策略配置，增加 prompt 等 Agent 专属配置。
/// </summary>
public sealed class DslAgentStepBuilder : DslCodeStepBuilder
{
    private string? _prompt;
    private string? _systemPrompt;
    private string? _routeName;
    private string? _eventType;
    private string? _heartbeatExtension;

    /// <summary>设置 Agent prompt 模板。</summary>
    public DslAgentStepBuilder WithPrompt(string prompt)
    {
        _prompt = prompt;
        return this;
    }

    /// <summary>设置 Agent system_prompt 模板。</summary>
    public DslAgentStepBuilder WithSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
        return this;
    }

    /// <summary>设置 Webhook 路由名称。</summary>
    public DslAgentStepBuilder WithRouteName(string routeName)
    {
        _routeName = routeName;
        return this;
    }

    /// <summary>设置 Webhook 事件类型。</summary>
    public DslAgentStepBuilder WithEventType(string eventType)
    {
        _eventType = eventType;
        return this;
    }

    /// <summary>设置心跳扩展时长。</summary>
    public DslAgentStepBuilder WithHeartbeatExtension(string heartbeatExtension)
    {
        _heartbeatExtension = heartbeatExtension;
        return this;
    }

    internal override StepHandlerDefaults BuildDefaults()
    {
        var baseDefaults = base.BuildDefaults();
        return baseDefaults with
        {
            Prompt = _prompt,
            SystemPrompt = _systemPrompt,
            RouteName = _routeName,
            EventType = _eventType,
            HeartbeatExtension = _heartbeatExtension,
        };
    }
}

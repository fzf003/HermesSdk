namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 默认步骤运行时配置提供者。
/// 合并优先级：YAML > Fluent API > Handler 虚属性 > 引擎默认值。
/// </summary>
internal sealed class DefaultStepRuntimeConfigProvider : IStepRuntimeConfigProvider
{
    private readonly IReadOnlyDictionary<Type, StepHandlerDefaults> _fluentDefaults;

    public DefaultStepRuntimeConfigProvider(
        IReadOnlyDictionary<Type, StepHandlerDefaults>? fluentDefaults = null)
    {
        _fluentDefaults = fluentDefaults ?? new Dictionary<Type, StepHandlerDefaults>();
    }

    public MergedStepRuntimeConfig GetConfig(
        IStepHandler handler,
        WorkflowDefinition? workflowDef,
        StepDefinition? stepDef)
    {
        var baseHandler = handler as StepHandlerBase;
        var agentHandler = handler as AgentStepHandler;

        // 读取 Fluent API 配置（如有）
        _fluentDefaults.TryGetValue(handler.GetType(), out var fluent);

        return new MergedStepRuntimeConfig
        {
            Timeout = !string.IsNullOrWhiteSpace(stepDef?.Timeout) ? stepDef!.Timeout
                    : !string.IsNullOrWhiteSpace(fluent?.Timeout) ? fluent.Timeout
                    : baseHandler?.Timeout,
            TimeoutAction = !string.IsNullOrWhiteSpace(stepDef?.TimeoutAction) ? stepDef!.TimeoutAction
                          : !string.IsNullOrWhiteSpace(fluent?.TimeoutAction) ? fluent.TimeoutAction
                          : baseHandler?.TimeoutAction,
            Retry = stepDef?.Retry ?? fluent?.Retry ?? baseHandler?.Retry,
            ErrorPolicy = !string.IsNullOrWhiteSpace(stepDef?.ErrorPolicy) ? stepDef!.ErrorPolicy
                        : !string.IsNullOrWhiteSpace(fluent?.ErrorPolicy) ? fluent.ErrorPolicy
                        : baseHandler?.ErrorPolicy,
            Prompt = !string.IsNullOrWhiteSpace(stepDef?.Prompt) ? stepDef!.Prompt
                   : !string.IsNullOrWhiteSpace(fluent?.Prompt) ? fluent.Prompt
                   : agentHandler?.Prompt,
            SystemPrompt = !string.IsNullOrWhiteSpace(stepDef?.SystemPrompt) ? stepDef!.SystemPrompt
                         : !string.IsNullOrWhiteSpace(fluent?.SystemPrompt) ? fluent.SystemPrompt
                         : agentHandler?.SystemPrompt,
            RouteName = !string.IsNullOrWhiteSpace(stepDef?.RouteName) ? stepDef!.RouteName
                      : !string.IsNullOrWhiteSpace(fluent?.RouteName) ? fluent.RouteName
                      : agentHandler?.RouteName,
            EventType = !string.IsNullOrWhiteSpace(stepDef?.EventType) ? stepDef!.EventType
                      : !string.IsNullOrWhiteSpace(fluent?.EventType) ? fluent.EventType
                      : agentHandler?.EventType,
            Notification = stepDef?.Notification ?? fluent?.Notification,
            HeartbeatExtension = !string.IsNullOrWhiteSpace(stepDef?.HeartbeatExtension) ? stepDef!.HeartbeatExtension
                               : !string.IsNullOrWhiteSpace(fluent?.HeartbeatExtension) ? fluent.HeartbeatExtension
                               : baseHandler?.HeartbeatExtension?.ToString(),
        };
    }
}

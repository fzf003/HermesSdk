namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// HumanApprovalStepHandler 的 Fluent 配置子构建器。
/// 由 WorkflowChainBuilder.AddHumanApprovalStep 内部使用，不直接实例化。
/// </summary>
public sealed class HumanApprovalStepBuilder<T> where T : HumanApprovalStepHandler
{
    private string? _timeout;
    private string? _timeoutAction;
    private RetryConfigYaml? _retry;
    private string? _errorPolicy;
    private string? _heartbeatExtension;
    private ApprovalNotificationConfig? _notification;

    /// <summary>设置超时时间</summary>
    public HumanApprovalStepBuilder<T> WithTimeout(string timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>设置超时行为</summary>
    public HumanApprovalStepBuilder<T> WithTimeoutAction(TimeoutAction action)
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
    public HumanApprovalStepBuilder<T> WithRetry(Action<RetryConfigBuilder> configure)
    {
        if (_retry is not null)
            throw new InvalidOperationException("WithRetry 已被调用过，每个步骤只允许配置一次重试策略。");
        var builder = new RetryConfigBuilder();
        configure(builder);
        _retry = builder.Build();
        return this;
    }

    /// <summary>设置错误处理策略</summary>
    public HumanApprovalStepBuilder<T> WithErrorPolicy(ErrorPolicy policy)
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

    /// <summary>设置心跳扩展时长（覆盖 Handler.HeartbeatExtension 虚属性）</summary>
    public HumanApprovalStepBuilder<T> WithHeartbeatExtension(string heartbeatExtension)
    {
        _heartbeatExtension = heartbeatExtension;
        return this;
    }

    /// <summary>设置审批通知配置（邮箱/IM/推送）</summary>
    public HumanApprovalStepBuilder<T> WithNotification(Action<ApprovalNotificationConfig> configure)
    {
        var notification = new ApprovalNotificationConfig();
        configure(notification);
        _notification = notification;
        return this;
    }

    /// <summary>产出 StepHandlerDefaults 供内部字典存储</summary>
    internal StepHandlerDefaults Build() => new()
    {
        Timeout = _timeout,
        TimeoutAction = _timeoutAction,
        Retry = _retry,
        ErrorPolicy = _errorPolicy,
        HeartbeatExtension = _heartbeatExtension,
        Notification = _notification,
    };
}

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// CodeStepHandler 的 Fluent 配置子构建器。
/// 由 WorkflowChainBuilder.AddCodeStep 内部使用，不直接实例化。
/// </summary>
public sealed class CodeStepBuilder<T> where T : CodeStepHandler
{
    private string? _timeout;
    private string? _timeoutAction;
    private RetryConfigYaml? _retry;
    private string? _errorPolicy;

    /// <summary>设置超时时间</summary>
    public CodeStepBuilder<T> WithTimeout(string timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>设置超时行为</summary>
    public CodeStepBuilder<T> WithTimeoutAction(TimeoutAction action)
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
    public CodeStepBuilder<T> WithRetry(Action<RetryConfigBuilder> configure)
    {
        if (_retry is not null)
            throw new InvalidOperationException("WithRetry 已被调用过，每个步骤只允许配置一次重试策略。");
        var builder = new RetryConfigBuilder();
        configure(builder);
        _retry = builder.Build();
        return this;
    }

    /// <summary>设置错误处理策略</summary>
    public CodeStepBuilder<T> WithErrorPolicy(ErrorPolicy policy)
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

    /// <summary>产出 StepHandlerDefaults 供内部字典存储</summary>
    internal StepHandlerDefaults Build() => new()
    {
        Timeout = _timeout,
        TimeoutAction = _timeoutAction,
        Retry = _retry,
        ErrorPolicy = _errorPolicy,
    };
}

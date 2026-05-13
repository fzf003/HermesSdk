namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// Code 步骤的 Fluent 配置构建器。由 <c>AddCodeStep(id, fn)</c> 返回。
/// 提供链式配置方法，最终产出 <see cref="StepHandlerDefaults"/> 参与运行时合并。
/// </summary>
public class DslCodeStepBuilder
{
    private string? _name;
    private string? _timeout;
    private string? _timeoutAction;
    private RetryConfigYaml? _retry;
    private string? _errorPolicy;

    /// <summary>设置步骤名称（描述性，不参与运行时逻辑）。</summary>
    public DslCodeStepBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>设置超时时间。</summary>
    public DslCodeStepBuilder WithTimeout(string timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>设置超时行为。</summary>
    public DslCodeStepBuilder WithTimeoutAction(TimeoutAction action)
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

    /// <summary>配置重试策略。</summary>
    public DslCodeStepBuilder WithRetry(Action<RetryConfigBuilder> configure)
    {
        if (_retry is not null)
            throw new InvalidOperationException("WithRetry 已被调用过，每个步骤只允许配置一次重试策略。");
        var builder = new RetryConfigBuilder();
        configure(builder);
        _retry = builder.Build();
        return this;
    }

    /// <summary>设置错误处理策略。</summary>
    public DslCodeStepBuilder WithErrorPolicy(ErrorPolicy policy)
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

    internal string? Name => _name;

    internal virtual StepHandlerDefaults BuildDefaults() => new()
    {
        Timeout = _timeout,
        TimeoutAction = _timeoutAction,
        Retry = _retry,
        ErrorPolicy = _errorPolicy,
    };
}

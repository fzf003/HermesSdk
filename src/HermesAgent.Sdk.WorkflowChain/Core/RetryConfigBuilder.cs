namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 策略感知的重试配置构建器。
/// 每个策略只暴露关联的参数，不相关参数无法配置。
/// </summary>
public sealed class RetryConfigBuilder
{
    private int _maxRetries = 3;
    private RetryPolicy? _policy;
    private string? _initialDelay;
    private double? _backoffFactor;
    private string? _maxDelay;

    /// <summary>立即重试策略，失败后立刻重试，无等待。</summary>
    /// <param name="maxRetries">最大重试次数（不含首次执行），默认 3。</param>
    public void Immediate(int maxRetries = 3)
    {
        _policy = RetryPolicy.Immediate;
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// 指数退避策略：每次重试延迟 = initial_delay × backoff_factor^attempt。
    /// 默认 initial_delay=1s, backoff_factor=2, max_delay=5min。
    /// </summary>
    /// <param name="maxRetries">最大重试次数，默认 3。</param>
    /// <param name="initialDelay">初始延迟（如 "1s", "00:00:01"），默认 1 秒。</param>
    /// <param name="backoffFactor">退避倍数，每次延迟乘以该值，默认 2.0。</param>
    /// <param name="maxDelay">最大延迟上限，默认 5 分钟。</param>
    public void ExponentialBackoff(int maxRetries = 3, string? initialDelay = null, double? backoffFactor = null, string? maxDelay = null)
    {
        _policy = RetryPolicy.ExponentialBackoff;
        _maxRetries = maxRetries;
        _initialDelay = initialDelay;
        _backoffFactor = backoffFactor;
        _maxDelay = maxDelay;
    }

    /// <summary>固定间隔重试策略：每次重试间隔固定。</summary>
    /// <param name="maxRetries">最大重试次数，默认 3。</param>
    /// <param name="initialDelay">重试间隔（如 "5s", "00:00:05"），默认 1 秒。</param>
    /// <param name="maxDelay">最大延迟上限，默认 5 分钟。</param>
    public void FixedInterval(int maxRetries = 3, string? initialDelay = null, string? maxDelay = null)
    {
        _policy = RetryPolicy.FixedInterval;
        _maxRetries = maxRetries;
        _initialDelay = initialDelay;
        _maxDelay = maxDelay;
    }

    internal RetryConfigYaml Build() => new()
    {
        MaxRetries = _maxRetries,
        Policy = _policy,
        InitialDelay = _initialDelay,
        BackoffFactor = _backoffFactor,
        MaxDelay = _maxDelay,
    };
}

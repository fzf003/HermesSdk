namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 重试配置。
/// </summary>
public class RetryConfig
{
    /// <summary>最大总尝试次数(默认3次，含首次)</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>重试策略(默认指数退避)</summary>
    public RetryPolicy Policy { get; set; } = RetryPolicy.ExponentialBackoff;

    /// <summary>初始延迟(默认1秒)</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>退避因子(指数退避使用,默认2.0)</summary>
    public double BackoffFactor { get; set; } = 2.0;

    /// <summary>最大延迟上限(默认5分钟)</summary>
    public TimeSpan? MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>自定义延迟计算器(Custom策略时使用)</summary>
    public Func<int, TimeSpan>? CustomDelayCalculator { get; set; }
}

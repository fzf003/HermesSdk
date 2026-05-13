namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 重试策略枚举。
/// </summary>
public enum RetryPolicy
{
    /// <summary>立即重试(无延迟)</summary>
    Immediate,

    /// <summary>指数退避: 1s → 2s → 4s → 8s</summary>
    ExponentialBackoff,

    /// <summary>固定间隔重试</summary>
    FixedInterval,

    /// <summary>自定义退避策略</summary>
    Custom,
}



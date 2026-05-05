namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 超时动作枚举。
/// </summary>
public enum TimeoutAction
{
    /// <summary>抛出异常(可能触发重试)</summary>
    Throw,

    /// <summary>标记失败(不重试)</summary>
    Fail,

    /// <summary>跳过步骤</summary>
    Skip,
}

/// <summary>
/// 超时配置。
/// </summary>
public class TimeoutConfig
{
    /// <summary>超时时长(默认5分钟)</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>超时动作(默认Fail)</summary>
    public TimeoutAction Action { get; set; } = TimeoutAction.Fail;
}

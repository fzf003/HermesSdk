namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 单次重试尝试记录。
/// </summary>
public class RetryAttempt
{
    /// <summary>尝试次数(0=首次执行,1=第1次重试)</summary>
    public int AttemptNumber { get; set; }

    /// <summary>尝试时间戳</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>退避延迟时长</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>错误消息(失败时)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>是否成功</summary>
    public bool Success { get; set; }
}

/// <summary>
/// 步骤重试历史。
/// </summary>
public class RetryHistory
{
    /// <summary>步骤ID</summary>
    public string StepId { get; set; } = "";

    /// <summary>工作流实例ID</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>重试尝试列表</summary>
    public List<RetryAttempt> Attempts { get; set; } = new();

    /// <summary>是否已耗尽所有重试次数</summary>
    public bool IsExhausted => Attempts.Count > 0 && !Attempts.Last().Success;

    /// <summary>成功时的尝试次数</summary>
    public int? SuccessfulAttempt => Attempts.FirstOrDefault(a => a.Success)?.AttemptNumber;
}

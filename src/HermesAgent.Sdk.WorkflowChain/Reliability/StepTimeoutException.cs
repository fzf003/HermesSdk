namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤超时异常。
/// </summary>
public class StepTimeoutException : Exception
{
    /// <summary>步骤ID</summary>
    public string StepId { get; }

    /// <summary>工作流实例ID</summary>
    public string InstanceId { get; }

    /// <summary>已运行时长</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// 创建步骤超时异常。
    /// </summary>
    public StepTimeoutException(string stepId, string instanceId, TimeSpan elapsed)
        : base($"步骤 {stepId} 超时 (已运行 {elapsed})")
    {
        StepId = stepId;
        InstanceId = instanceId;
        Elapsed = elapsed;
    }
}

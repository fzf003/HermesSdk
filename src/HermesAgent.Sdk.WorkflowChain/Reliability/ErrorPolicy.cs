namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 错误处理策略枚举。
/// </summary>
public enum ErrorPolicy
{
    /// <summary>快速失败 - 立即终止工作流</summary>
    FailFast,

    /// <summary>继续执行 - 记录错误但继续后续步骤</summary>
    ContinueOnError,

    /// <summary>跳过失败分支 - 并行分支中跳过失败分支</summary>
    SkipFailedBranch,
}

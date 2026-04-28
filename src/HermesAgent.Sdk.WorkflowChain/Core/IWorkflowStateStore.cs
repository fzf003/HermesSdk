namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流状态持久化接口。
///
/// 实现选项：
///   InMemoryStateStore  — Phase 1 默认，内存存储，重启丢失
///   RedisStateStore     — Phase 2 正式，Redis 持久化，重启可恢复
///   SqliteStateStore    — 可选，单机部署
///   PostgresStateStore  — 可选，云原生
/// </summary>
public interface IWorkflowStateStore
{
    /// <summary>保存工作流检查点（创建或更新）</summary>
    Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default);

    /// <summary>加载工作流检查点（重启恢复时使用）</summary>
    Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default);

    /// <summary>删除工作流检查点（工作流完成后清理）</summary>
    Task DeleteAsync(string instanceId, CancellationToken ct = default);

    /// <summary>获取所有 running 状态的实例 ID（用于启动恢复）</summary>
    Task<List<string>> ListRunningAsync(CancellationToken ct = default);
}

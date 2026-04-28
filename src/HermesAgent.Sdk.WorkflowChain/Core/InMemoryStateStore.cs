using System.Collections.Concurrent;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 内存状态存储 — Phase 1 默认实现。
/// 通过 ConcurrentDictionary 保存，重启丢失，不依赖外部基础设施。
/// </summary>
public class InMemoryStateStore : IWorkflowStateStore
{
    private readonly ConcurrentDictionary<string, WorkflowCheckpoint> _store = new();

    public Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        _store[checkpoint.InstanceId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
    {
        _store.TryGetValue(instanceId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task DeleteAsync(string instanceId, CancellationToken ct = default)
    {
        _store.TryRemove(instanceId, out _);
        return Task.CompletedTask;
    }

    public Task<List<string>> ListRunningAsync(CancellationToken ct = default)
    {
        var running = _store.Values
            .Where(c => c.Status == "running")
            .Select(c => c.InstanceId)
            .ToList();
        return Task.FromResult(running);
    }
}

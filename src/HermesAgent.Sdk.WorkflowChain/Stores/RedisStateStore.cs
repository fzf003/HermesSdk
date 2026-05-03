using System.Text.Json;
using StackExchange.Redis;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 基于 Redis 的工作流状态持久化实现。
/// 使用事务操作保证原子性，支持 TTL 自动清理和分布式锁。
/// </summary>
public class RedisStateStore : IWorkflowStateStore, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly bool _ownsConnection;
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    public RedisStateStore(IConnectionMultiplexer redis, int dbIndex = 0)
    {
        _redis = redis;
        _db = redis.GetDatabase(dbIndex);
        _ownsConnection = false;
    }

    public RedisStateStore(string connectionString, int dbIndex = 0)
        : this(ConnectionMultiplexer.Connect(connectionString), dbIndex)
    {
        _ownsConnection = true;
    }

    public void Dispose()
    {
        if (_ownsConnection)
            _redis?.Dispose();
    }

    public async Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(checkpoint);
        var chkKey = $"wf:chk:{checkpoint.InstanceId}";

        var tx = _db.CreateTransaction();

        // 保存检查点
        _ = tx.StringSetAsync(chkKey, json);

        // 维护 running 集合
        if (checkpoint.Status == "running")
        {
            _ = tx.SetAddAsync("wf:running", checkpoint.InstanceId);
            _ = tx.SetRemoveAsync("wf:timed-out", checkpoint.InstanceId);
        }
        else if (checkpoint.Status == "timed-out")
        {
            _ = tx.SetRemoveAsync("wf:running", checkpoint.InstanceId);
            _ = tx.SetAddAsync("wf:timed-out", checkpoint.InstanceId);
        }
        else
        {
            _ = tx.SetRemoveAsync("wf:running", checkpoint.InstanceId);
            _ = tx.SetRemoveAsync("wf:timed-out", checkpoint.InstanceId);
            // 已完成实例设置 TTL
            _ = tx.KeyExpireAsync(chkKey, CompletedTtl);
        }

        // 维护 in_flight 集合
        var inFlightKey = $"wf:in_flight:{checkpoint.InstanceId}";
        _ = tx.KeyDeleteAsync(inFlightKey);
        if (checkpoint.InFlightStepIds.Count > 0)
        {
            var values = checkpoint.InFlightStepIds.Select(id => (RedisValue)id).ToArray();
            _ = tx.SetAddAsync(inFlightKey, values);
        }

        await tx.ExecuteAsync();
    }

    public async Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync($"wf:chk:{instanceId}");
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<WorkflowCheckpoint>(json.ToString());
    }

    public async Task DeleteAsync(string instanceId, CancellationToken ct = default)
    {
        var tx = _db.CreateTransaction();
        _ = tx.KeyDeleteAsync($"wf:chk:{instanceId}");
        _ = tx.SetRemoveAsync("wf:running", instanceId);
        _ = tx.KeyDeleteAsync($"wf:in_flight:{instanceId}");
        await tx.ExecuteAsync();
    }

    public async Task<List<string>> ListRunningAsync(CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync("wf:running");
        return members.Select(m => m.ToString()).ToList();
    }

    public async Task<List<string>> ListTimedOutAsync(CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync("wf:timed-out");
        return members.Select(m => m.ToString()).ToList();
    }

    /// <summary>
    /// 获取分布式锁，防止同一实例的并发回调重复处理。
    /// </summary>
    /// <param name="instanceId">工作流实例 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>锁句柄，使用完毕后 await using DisposeAsync 释放</returns>
    public async Task<IAsyncDisposable> AcquireLockAsync(string instanceId, CancellationToken ct = default)
    {
        var lockKey = $"wf:lock:{instanceId}";
        var lockValue = Guid.NewGuid().ToString("N");

        while (!ct.IsCancellationRequested)
        {
            var acquired = await _db.StringSetAsync(lockKey, lockValue, LockExpiry, When.NotExists);
            if (acquired)
            {
                return new RedisLock(_db, lockKey, lockValue);
            }
            await Task.Delay(100, ct);
        }
        throw new OperationCanceledException(ct);
    }

    /// <summary>
    /// Lua 脚本：原子性 check-and-delete 释放锁。
    /// 只有持有锁的调用方才能释放，避免 TTL 到期后误删其他实例的锁。
    /// </summary>
    private const string UnlockScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    private class RedisLock : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _value;

        public RedisLock(IDatabase db, string key, string value)
        {
            _db = db;
            _key = key;
            _value = value;
        }

        public async ValueTask DisposeAsync()
        {
            // Lua 脚本保证 GET + DEL 原子性，防止误删其他实例的锁
            await _db.ScriptEvaluateAsync(UnlockScript,
                new RedisKey[] { _key },
                new RedisValue[] { _value });
        }
    }
}

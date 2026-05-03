using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 基于 SQLite 的工作流状态持久化实现。
/// 零外部依赖，启用 WAL 模式支持并发读写。
/// </summary>
public class SqliteStateStore : IWorkflowStateStore
{
    private readonly string _connectionString;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteStateStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            // 启用 WAL 模式
            using var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection);
            await walCmd.ExecuteNonQueryAsync(ct);

            // 创建表
            using var cmd = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS workflow_checkpoints (
                    instance_id    TEXT PRIMARY KEY,
                    checkpoint     TEXT NOT NULL,
                    status         TEXT NOT NULL,
                    last_heartbeat TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_status ON workflow_checkpoints(status);
            ", connection);
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        var json = JsonSerializer.Serialize(checkpoint);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand(@"
            INSERT INTO workflow_checkpoints (instance_id, checkpoint, status, last_heartbeat)
            VALUES (@id, @checkpoint, @status, @heartbeat)
            ON CONFLICT(instance_id) DO UPDATE SET
                checkpoint = @checkpoint,
                status = @status,
                last_heartbeat = @heartbeat
        ", connection);

        cmd.Parameters.AddWithValue("@id", checkpoint.InstanceId);
        cmd.Parameters.AddWithValue("@checkpoint", json);
        cmd.Parameters.AddWithValue("@status", checkpoint.Status);
        cmd.Parameters.AddWithValue("@heartbeat", checkpoint.LastHeartbeat.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand(
            "SELECT checkpoint FROM workflow_checkpoints WHERE instance_id = @id", connection);
        cmd.Parameters.AddWithValue("@id", instanceId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;

        return JsonSerializer.Deserialize<WorkflowCheckpoint>(result.ToString()!);
    }

    public async Task DeleteAsync(string instanceId, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand(
            "DELETE FROM workflow_checkpoints WHERE instance_id = @id", connection);
        cmd.Parameters.AddWithValue("@id", instanceId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<string>> ListRunningAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand(
            "SELECT instance_id FROM workflow_checkpoints WHERE status = 'running'", connection);

        var ids = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    public async Task<List<string>> ListTimedOutAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand(
            "SELECT instance_id FROM workflow_checkpoints WHERE status = 'timed-out'", connection);

        var ids = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }
}

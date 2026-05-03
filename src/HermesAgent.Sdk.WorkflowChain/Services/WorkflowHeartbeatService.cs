using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流心跳检测后台服务。
/// 每分钟扫描所有 running 实例，超过有效阈值的标记为 timed-out。
/// 有效阈值 = max(全局阈值, max(InFlight步骤的HeartbeatExtension))。
/// </summary>
public class WorkflowHeartbeatService : IHostedService, IDisposable
{
    private readonly WorkflowEngine _engine;
    private readonly IWorkflowStateStore _stateStore;
    private readonly ILogger<WorkflowHeartbeatService> _logger;
    private readonly TimeSpan _heartbeatThreshold;
    private CancellationTokenSource? _stoppingCts;
    private Task? _executingTask;

    public WorkflowHeartbeatService(
        WorkflowEngine engine,
        IWorkflowStateStore stateStore,
        ILogger<WorkflowHeartbeatService> logger,
        TimeSpan heartbeatThreshold)
    {
        _engine = engine;
        _stateStore = stateStore;
        _logger = logger;
        _heartbeatThreshold = heartbeatThreshold;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = ScanLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts == null) return;

        _stoppingCts.Cancel();

        // 等待后台扫描循环完全退出，确保优雅关闭
        if (_executingTask != null)
        {
            try { await _executingTask; }
            catch (OperationCanceledException) { /* 预期的关闭行为：取消导致 ScanLoopAsync 退出 */ }
        }
    }

    public void Dispose()
    {
        _stoppingCts?.Dispose();
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("心跳检测服务启动，扫描间隔: 1 分钟，超时阈值: {Threshold}", _heartbeatThreshold);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanAndTimeoutAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "心跳检测扫描异常");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }

        _logger.LogInformation("心跳检测服务停止");
    }

    private async Task ScanAndTimeoutAsync(CancellationToken ct)
    {
        var runningIds = await _stateStore.ListRunningAsync(ct);
        if (runningIds.Count == 0) return;

        var timeoutCount = 0;

        foreach (var instanceId in runningIds)
        {
            // 委托引擎在分布式锁保护下执行超时标记，避免 TOCTOU 竞态
            try
            {
                var checkpoint = await _stateStore.LoadAsync(instanceId, ct);
                if (checkpoint == null) continue;

                // 计算有效阈值：考虑 InFlight 步骤的 HeartbeatExtension
                var effectiveThreshold = _engine.GetEffectiveHeartbeatThreshold(instanceId, _heartbeatThreshold);

                var timeSinceHeartbeat = DateTime.UtcNow - checkpoint.LastHeartbeat;
                if (timeSinceHeartbeat <= effectiveThreshold) continue;

                await _engine.MarkInstanceTimedOutAsync(instanceId, effectiveThreshold, ct);
                timeoutCount++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "心跳超时标记失败: {InstanceId}", instanceId);
            }
        }

        if (timeoutCount > 0)
        {
            _logger.LogWarning("本轮心跳检测标记 {Count} 个实例为 timed-out", timeoutCount);
        }
    }
}

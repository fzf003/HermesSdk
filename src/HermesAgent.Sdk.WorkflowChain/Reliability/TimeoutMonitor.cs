using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 超时监控器 - 监控步骤执行超时。
/// </summary>
public class TimeoutMonitor : IDisposable
{
    private readonly Timer _timer;
    private readonly CancellationTokenSource _cts;
    private readonly string _stepId;
    private readonly string _instanceId;
    private readonly TimeoutConfig _config;
    private readonly ILogger _logger;
    private volatile bool _completed;

    /// <summary>超时事件</summary>
    public event Action<string, string, TimeSpan>? OnTimeout;

    /// <summary>
    /// 创建超时监控器。
    /// </summary>
    public TimeoutMonitor(
        string stepId,
        string instanceId,
        TimeoutConfig config,
        ILogger logger)
    {
        _stepId = stepId;
        _instanceId = instanceId;
        _config = config;
        _logger = logger;
        _cts = new CancellationTokenSource();

        _timer = new Timer(
            callback: _ => HandleTimeout(),
            state: null,
            dueTime: (int)config.Duration.TotalMilliseconds,
            period: Timeout.Infinite);
    }

    private void HandleTimeout()
    {
        if (_completed) return;

        _logger.LogWarning(
            "步骤 {StepId} 超时 (阈值: {Duration})",
            _stepId, _config.Duration);

        OnTimeout?.Invoke(_stepId, _instanceId, _config.Duration);

        // 取消步骤执行
        _cts.Cancel();
    }

    /// <summary>标记步骤已完成,停止监控。</summary>
    public void MarkCompleted()
    {
        _completed = true;
        _timer.Dispose();
        _cts.Dispose();
    }

    /// <summary>获取取消令牌,传递给步骤执行。</summary>
    public CancellationToken CancellationToken => _cts.Token;

    public void Dispose()
    {
        _timer.Dispose();
        _cts.Dispose();
    }
}

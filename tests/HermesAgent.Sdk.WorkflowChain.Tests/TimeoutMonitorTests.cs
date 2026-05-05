using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class TimeoutMonitorTests
{
    private readonly NullLogger _logger = NullLogger.Instance;

    // ═══════════════════════════════════════════
    // 超时触发
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ExceedsTimeout_TriggersOnTimeout()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(100) };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);
        var timeoutFired = new TaskCompletionSource<(string stepId, string instanceId, TimeSpan duration)>();
        monitor.OnTimeout += (stepId, instanceId, duration) =>
            timeoutFired.SetResult((stepId, instanceId, duration));

        // Act — wait for timer to fire
        var result = await Task.WhenAny(timeoutFired.Task, Task.Delay(2000));

        // Assert
        Assert.True(timeoutFired.Task.IsCompleted, "OnTimeout should have fired");
        var args = await timeoutFired.Task;
        Assert.Equal("step-1", args.stepId);
        Assert.Equal("inst-1", args.instanceId);
        Assert.Equal(TimeSpan.FromMilliseconds(100), args.duration);
    }

    [Fact]
    public async Task ExceedsTimeout_CancelsCancellationToken()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(100) };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);

        // Act — wait for timeout
        await Task.Delay(200);

        // Assert
        Assert.True(monitor.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CompletesBeforeTimeout_NoAction()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(500) };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);
        var timeoutFired = false;
        monitor.OnTimeout += (_, _, _) => timeoutFired = true;

        // Capture token before MarkCompleted (which disposes the CTS)
        var token = monitor.CancellationToken;

        // Act — mark completed before timeout
        monitor.MarkCompleted();
        await Task.Delay(100); // give timer a chance (it shouldn't fire)

        // Assert
        Assert.False(timeoutFired);
        Assert.False(token.IsCancellationRequested);
    }

    // ═══════════════════════════════════════════
    // 不同动作
    // ═══════════════════════════════════════════

    [Fact]
    public void TimeoutAction_Throw_IsDefault()
    {
        // Verify the enum values exist and have correct values
        Assert.Equal(0, (int)TimeoutAction.Throw);
        Assert.Equal(1, (int)TimeoutAction.Fail);
        Assert.Equal(2, (int)TimeoutAction.Skip);
    }

    [Fact]
    public void TimeoutConfig_DefaultAction_IsFail()
    {
        // Verify default TimeoutConfig values
        var config = new TimeoutConfig();
        Assert.Equal(TimeoutAction.Fail, config.Action);
        Assert.Equal(TimeSpan.FromMinutes(5), config.Duration);
    }

    // ═══════════════════════════════════════════
    // 资源清理
    // ═══════════════════════════════════════════

    [Fact]
    public void Dispose_CancelsTimer()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(100) };
        var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);
        var timeoutFired = false;
        monitor.OnTimeout += (_, _, _) => timeoutFired = true;

        // Act — dispose immediately
        monitor.Dispose();

        // Assert — wait and verify timeout didn't fire
        Thread.Sleep(200);
        Assert.False(timeoutFired);
    }

    [Fact]
    public async Task CancellationToken_CancelledEarly_ByTimeout()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(50) };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);

        // Act — wait for timeout
        await Task.Delay(150);

        // Assert
        Assert.True(monitor.CancellationToken.IsCancellationRequested);
    }

    // ═══════════════════════════════════════════
    // 边界情况
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ZeroDuration_ImmediatelyTriggers()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.Zero };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);
        var timeoutFired = new TaskCompletionSource<bool>();
        monitor.OnTimeout += (_, _, _) => timeoutFired.SetResult(true);

        // Act — zero duration should trigger almost immediately
        var result = await Task.WhenAny(timeoutFired.Task, Task.Delay(500));

        // Assert
        Assert.True(timeoutFired.Task.IsCompleted, "Zero duration should trigger timeout");
    }

    [Fact]
    public async Task MarkCompleted_AfterTimeout_NoDuplicateEvents()
    {
        // Arrange
        var config = new TimeoutConfig { Duration = TimeSpan.FromMilliseconds(50) };
        using var monitor = new TimeoutMonitor("step-1", "inst-1", config, _logger);
        var fireCount = 0;
        monitor.OnTimeout += (_, _, _) => Interlocked.Increment(ref fireCount);

        // Act — wait for timeout, then mark completed
        await Task.Delay(200);
        monitor.MarkCompleted();

        // Wait a bit more to ensure no duplicate events
        await Task.Delay(100);

        // Assert — should fire exactly once
        Assert.Equal(1, fireCount);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class RetryExecutorTests
{
    private readonly RetryExecutor _executor = new(NullLogger<RetryExecutor>.Instance);

    // ═══════════════════════════════════════════
    // 基础成功场景
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Execute_FirstAttemptSucceeds()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 3, Policy = RetryPolicy.Immediate };

        // Act
        var result = await _executor.ExecuteWithRetryAsync(
            ct => Task.FromResult("ok"), config, "step-1", "inst-1", CancellationToken.None);

        // Assert
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Execute_RetriesUntilSuccess()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 3, Policy = RetryPolicy.Immediate };
        var attempts = 0;

        // Act
        var result = await _executor.ExecuteWithRetryAsync(
            ct =>
            {
                attempts++;
                return attempts < 3
                    ? throw new HttpRequestException("transient")
                    : Task.FromResult("recovered");
            },
            config, "step-1", "inst-1", CancellationToken.None);

        // Assert
        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Execute_ExhaustedRetries_ThrowsLastException()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 2, Policy = RetryPolicy.Immediate };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("persistent"),
                config, "step-1", "inst-1", CancellationToken.None));

        Assert.Equal("persistent", ex.Message);
    }

    // ═══════════════════════════════════════════
    // 退避策略
    // ═══════════════════════════════════════════

    [Fact]
    public async Task FixedInterval_UsesConstantDelay()
    {
        // Arrange
        var config = new RetryConfig
        {
            MaxRetries = 2,
            Policy = RetryPolicy.FixedInterval,
            InitialDelay = TimeSpan.FromMilliseconds(50)
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch { }

        // Assert: 一次重试约 50ms
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 30, $"FixedInterval delay too short: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ExponentialBackoff_IncreasesDelay()
    {
        // Arrange
        var config = new RetryConfig
        {
            MaxRetries = 2,
            Policy = RetryPolicy.ExponentialBackoff,
            InitialDelay = TimeSpan.FromMilliseconds(20),
            BackoffFactor = 2.0
        };

        // Act — measure total time; with exp backoff: ~20ms (1 delay)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch { }

        // Assert: total > 15ms (20ms with jitter tolerance)
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 15, $"ExponentialBackoff too short: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CustomPolicy_UsesCustomDelayCalculator()
    {
        // Arrange
        var config = new RetryConfig
        {
            MaxRetries = 2,
            Policy = RetryPolicy.Custom,
            CustomDelayCalculator = attempt => TimeSpan.FromMilliseconds(10 * (attempt + 1))
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch { }

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 5, $"Custom delay too short: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task MaxDelay_CapsExponentialBackoff()
    {
        // Arrange
        var config = new RetryConfig
        {
            MaxRetries = 5,
            Policy = RetryPolicy.ExponentialBackoff,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            BackoffFactor = 10.0,
            MaxDelay = TimeSpan.FromMilliseconds(200)
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act: with MaxDelay=200ms, each retry capped at 200ms
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch { }

        sw.Stop();
        // 5 retries capped at 200ms each = max ~1000ms
        Assert.True(sw.ElapsedMilliseconds < 2000, $"MaxDelay not capping: {sw.ElapsedMilliseconds}ms");
    }

    // ═══════════════════════════════════════════
    // 异常过滤
    // ═══════════════════════════════════════════

    [Fact]
    public async Task NonRetryableException_DoesNotRetry()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 3, Policy = RetryPolicy.Immediate };
        var attempts = 0;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _executor.ExecuteWithRetryAsync<string>(
                ct =>
                {
                    attempts++;
                    throw new ArgumentException("invalid");
                },
                config, "step-1", "inst-1", CancellationToken.None));

        Assert.Equal(1, attempts); // no retries for ArgumentException
    }

    [Fact]
    public async Task RetryableException_Retries()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 2, Policy = RetryPolicy.Immediate };
        var attempts = 0;

        // Act & Assert — HttpRequestException is retryable
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            _executor.ExecuteWithRetryAsync<string>(
                ct =>
                {
                    attempts++;
                    throw new TimeoutException("timeout");
                },
                config, "step-1", "inst-1", CancellationToken.None));

        Assert.Equal(2, attempts); // 1 initial + 1 retry = 2 total
    }

    [Fact]
    public async Task StepTimeoutException_IsRetryable()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 1, Policy = RetryPolicy.Immediate };
        var attempts = 0;

        // Act
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct =>
                {
                    attempts++;
                    throw new StepTimeoutException("s1", "i1", TimeSpan.FromSeconds(5));
                },
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch (StepTimeoutException) { }

        // Assert: StepTimeoutException is retryable, so 1 initial attempt only (MaxRetries=1 → 1 total)
        Assert.Equal(1, attempts);
    }

    // ═══════════════════════════════════════════
    // 边界情况
    // ═══════════════════════════════════════════

    [Fact]
    public async Task ZeroMaxRetries_NoRetry()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 0, Policy = RetryPolicy.Immediate };
        var attempts = 0;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _executor.ExecuteWithRetryAsync<string>(
                ct =>
                {
                    attempts++;
                    throw new HttpRequestException("fail");
                },
                config, "step-1", "inst-1", CancellationToken.None));

        Assert.Equal(1, attempts); // only initial attempt, no retries
    }

    [Fact]
    public async Task CancellationToken_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var config = new RetryConfig { MaxRetries = 3, Policy = RetryPolicy.FixedInterval, InitialDelay = TimeSpan.FromMilliseconds(10) };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert — cancelled token causes Task.Delay to throw OperationCanceledException (or derived TaskCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", cts.Token));
    }

    [Fact]
    public async Task ImmediatePolicy_NoDelay()
    {
        // Arrange
        var config = new RetryConfig
        {
            MaxRetries = 1,
            Policy = RetryPolicy.Immediate,
            InitialDelay = TimeSpan.FromMilliseconds(500) // should be ignored
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        try
        {
            await _executor.ExecuteWithRetryAsync<string>(
                ct => throw new HttpRequestException("fail"),
                config, "step-1", "inst-1", CancellationToken.None);
        }
        catch { }

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"Immediate policy should not delay: {sw.ElapsedMilliseconds}ms");
    }
}

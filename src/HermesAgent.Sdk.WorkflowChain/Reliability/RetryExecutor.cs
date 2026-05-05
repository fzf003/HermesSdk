using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 重试执行器 - 为步骤执行提供重试逻辑。
/// </summary>
public class RetryExecutor
{
    private readonly ILogger<RetryExecutor> _logger;

    /// <summary>
    /// 创建重试执行器实例。
    /// </summary>
    public RetryExecutor(ILogger<RetryExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 使用重试策略执行操作。
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="config">重试配置</param>
    /// <param name="stepId">步骤ID</param>
    /// <param name="instanceId">工作流实例ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        RetryConfig config,
        string stepId,
        string instanceId,
        CancellationToken ct)
    {
        var history = new RetryHistory { StepId = stepId, InstanceId = instanceId };
        Exception? lastException = null;

        for (int attempt = 0; attempt < Math.Max(1, config.MaxRetries); attempt++)
        {
            try
            {
                var result = await operation(ct);

                // 记录成功
                history.Attempts.Add(new RetryAttempt
                {
                    AttemptNumber = attempt,
                    Timestamp = DateTime.UtcNow,
                    Success = true,
                    Delay = attempt > 0 ? CalculateDelay(config, attempt - 1) : TimeSpan.Zero
                });

                if (attempt > 0)
                {
                    _logger.LogInformation(
                        "步骤 {StepId} 在第 {Attempt} 次重试后成功",
                        stepId, attempt);
                }

                return result;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt + 1 < Math.Max(1, config.MaxRetries))
            {
                lastException = ex;

                // 记录失败
                history.Attempts.Add(new RetryAttempt
                {
                    AttemptNumber = attempt,
                    Timestamp = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Delay = attempt > 0 ? CalculateDelay(config, attempt - 1) : TimeSpan.Zero
                });

                var delay = CalculateDelay(config, attempt);
                _logger.LogWarning(ex,
                    "步骤 {StepId} 第 {Attempt} 次尝试失败,{Delay}ms 后重试: {Error}",
                    stepId, attempt + 1, delay.TotalMilliseconds, ex.Message);

                // 等待后重试
                await Task.Delay(delay, ct);
            }
        }

        // 所有重试耗尽,抛出异常
        _logger.LogError(lastException,
            "步骤 {StepId} 重试耗尽 (共 {TotalAttempts} 次尝试)",
            stepId, Math.Max(1, config.MaxRetries));

        throw lastException ?? new InvalidOperationException("重试执行失败");
    }

    /// <summary>
    /// 计算退避延迟。
    /// </summary>
    private TimeSpan CalculateDelay(RetryConfig config, int attempt)
    {
        var delay = config.Policy switch
        {
            RetryPolicy.Immediate => TimeSpan.Zero,
            RetryPolicy.FixedInterval => config.InitialDelay,
            RetryPolicy.ExponentialBackoff => CalculateExponentialBackoff(config, attempt),
            RetryPolicy.Custom when config.CustomDelayCalculator != null => config.CustomDelayCalculator(attempt),
            _ => config.InitialDelay
        };

        // 限制最大延迟
        if (config.MaxDelay.HasValue && delay > config.MaxDelay.Value)
            delay = config.MaxDelay.Value;

        return delay;
    }

    /// <summary>
    /// 计算指数退避延迟(含随机抖动)。
    /// </summary>
    private TimeSpan CalculateExponentialBackoff(RetryConfig config, int attempt)
    {
        var delayMs = config.InitialDelay.TotalMilliseconds * Math.Pow(config.BackoffFactor, attempt);

        // 添加±10%随机抖动避免雪崩
        var jitter = (Random.Shared.NextDouble() - 0.5) * 0.2;
        delayMs *= (1 + jitter);

        return TimeSpan.FromMilliseconds(delayMs);
    }

    /// <summary>
    /// 判断异常是否可重试。
    /// </summary>
    private bool IsRetryable(Exception ex)
    {
        // 网络故障、超时、取消可重试
        if (ex is HttpRequestException) return true;
        if (ex is TimeoutException) return true;
        if (ex is OperationCanceledException) return true;
        if (ex is StepTimeoutException) return true;
        if (ex is StepRetryException) return true;

        // 业务逻辑错误不重试
        if (ex is ArgumentException) return false;
        if (ex is InvalidOperationException) return false;

        // 默认不重试未知异常
        return false;
    }
}

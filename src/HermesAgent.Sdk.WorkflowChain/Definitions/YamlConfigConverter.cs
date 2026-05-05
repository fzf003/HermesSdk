namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// YAML 配置转换辅助类 - 将 YAML 定义转换为运行时配置。
///
/// 映射关系：
/// - retry.policy: "immediate" → RetryPolicy.Immediate, "fixed"/"fixed_interval" → RetryPolicy.FixedInterval,
///   "exponential_backoff"/"exponential" → RetryPolicy.ExponentialBackoff, "custom" → RetryPolicy.Custom
/// - timeout_action: "throw" → TimeoutAction.Throw, "fail" → TimeoutAction.Fail, "skip" → TimeoutAction.Skip
/// - error_policy: "failfast"/"fail_fast"/"failimmediately"/"fail_immediately" → ErrorPolicy.FailFast,
///   "continueonerror"/"continue_on_error"/"retry" → ErrorPolicy.ContinueOnError,
///   "skipfailedbranch"/"skip_failed_branch"/"skip" → ErrorPolicy.SkipFailedBranch
/// - 时间简写: "30s" → 30秒, "5m" → 5分钟, "2h" → 2小时, "1d" → 1天
/// </summary>
public static class YamlConfigConverter
{
    /// <summary>
    /// 从 YAML RetryConfigYaml 转换为运行时 RetryConfig。
    /// </summary>
    public static RetryConfig ConvertRetryConfig(RetryConfigYaml? yamlConfig)
    {
        if (yamlConfig == null)
            return new RetryConfig();

        var config = new RetryConfig
        {
            MaxRetries = yamlConfig.MaxRetries,
            BackoffFactor = yamlConfig.BackoffFactor
        };

        // 解析重试策略
        if (!string.IsNullOrEmpty(yamlConfig.Policy))
        {
            config.Policy = yamlConfig.Policy.ToLower() switch
            {
                "fixed" or "fixed_interval" => RetryPolicy.FixedInterval,
                "exponential_backoff" or "exponential" => RetryPolicy.ExponentialBackoff,
                "immediate" => RetryPolicy.Immediate,
                "custom" => RetryPolicy.Custom,
                _ => RetryPolicy.ExponentialBackoff
            };
        }

        // 解析初始延迟
        if (!string.IsNullOrEmpty(yamlConfig.InitialDelay))
        {
            config.InitialDelay = ParseTimeSpan(yamlConfig.InitialDelay);
        }

        // 解析最大延迟
        if (!string.IsNullOrEmpty(yamlConfig.MaxDelay))
        {
            config.MaxDelay = ParseTimeSpan(yamlConfig.MaxDelay);
        }

        return config;
    }

    /// <summary>
    /// 从 YAML Timeout 字符串转换为 TimeoutConfig。
    /// </summary>
    public static TimeoutConfig? ConvertTimeoutConfig(string? timeoutString, string? timeoutActionString)
    {
        if (string.IsNullOrEmpty(timeoutString))
            return null;

        var config = new TimeoutConfig
        {
            Duration = ParseTimeSpan(timeoutString)
        };

        // 解析超时动作
        if (!string.IsNullOrEmpty(timeoutActionString))
        {
            config.Action = timeoutActionString.ToLower() switch
            {
                "throw" => TimeoutAction.Throw,
                "fail" => TimeoutAction.Fail,
                "skip" => TimeoutAction.Skip,
                _ => TimeoutAction.Fail
            };
        }

        return config;
    }

    /// <summary>
    /// 从 YAML ErrorPolicy 字符串转换为 ErrorPolicy 枚举。
    /// </summary>
    public static ErrorPolicy? ConvertErrorPolicy(string? errorPolicyString)
    {
        if (string.IsNullOrEmpty(errorPolicyString))
            return null;

        return errorPolicyString.ToLower() switch
        {
            "failfast" or "fail_fast" or "failimmediately" or "fail_immediately" => ErrorPolicy.FailFast,
            "continueonerror" or "continue_on_error" or "retry" => ErrorPolicy.ContinueOnError,
            "skipfailedbranch" or "skip_failed_branch" or "skip" => ErrorPolicy.SkipFailedBranch,
            _ => ErrorPolicy.FailFast
        };
    }

    /// <summary>
    /// 解析 TimeSpan 字符串（支持多种格式）。
    /// 支持的格式：
    /// - "10:00" -> 10分钟
    /// - "00:10:00" -> 10分钟
    /// - "1.00:00:00" -> 1天
    /// - "30s" -> 30秒
    /// - "5m" -> 5分钟
    /// - "2h" -> 2小时
    /// - "1d" -> 1天
    /// </summary>
    public static TimeSpan ParseTimeSpan(string timeSpanString)
    {
        if (string.IsNullOrWhiteSpace(timeSpanString))
            throw new ArgumentException("时间字符串不能为空", nameof(timeSpanString));

        // 尝试标准 TimeSpan 解析
        if (TimeSpan.TryParse(timeSpanString, out var result))
            return result;

        // 尝试简写格式
        timeSpanString = timeSpanString.Trim().ToLowerInvariant();
        
        if (timeSpanString.EndsWith("s"))
        {
            if (double.TryParse(timeSpanString[..^1], out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        else if (timeSpanString.EndsWith("m"))
        {
            if (double.TryParse(timeSpanString[..^1], out var minutes))
                return TimeSpan.FromMinutes(minutes);
        }
        else if (timeSpanString.EndsWith("h"))
        {
            if (double.TryParse(timeSpanString[..^1], out var hours))
                return TimeSpan.FromHours(hours);
        }
        else if (timeSpanString.EndsWith("d"))
        {
            if (double.TryParse(timeSpanString[..^1], out var days))
                return TimeSpan.FromDays(days);
        }

        throw new FormatException($"无法解析时间字符串: {timeSpanString}");
    }
}

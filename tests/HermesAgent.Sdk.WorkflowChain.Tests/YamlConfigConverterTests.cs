using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class YamlConfigConverterTests
{
    // ═══════════════════════════════════════════
    // RetryConfig 转换
    // ═══════════════════════════════════════════

    [Fact]
    public void ConvertRetryConfig_Null_ReturnsDefault()
    {
        var config = YamlConfigConverter.ConvertRetryConfig(null);
        Assert.Equal(3, config.MaxRetries); // 默认 3 次
        Assert.Equal(RetryPolicy.ExponentialBackoff, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_FixedInterval()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 3, Policy = "fixed" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(RetryPolicy.FixedInterval, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_FixedIntervalAlias()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 3, Policy = "fixed_interval" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(RetryPolicy.FixedInterval, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_ExponentialBackoff()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 5, Policy = "exponential_backoff" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(RetryPolicy.ExponentialBackoff, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_ExponentialAlias()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 5, Policy = "exponential" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(RetryPolicy.ExponentialBackoff, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_Immediate()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 2, Policy = "immediate" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(RetryPolicy.Immediate, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_Custom()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 1, Policy = "custom" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(RetryPolicy.Custom, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithPolicy_Unknown_DefaultsToExponential()
    {
        var yaml = new RetryConfigYaml { MaxRetries = 1, Policy = "unknown_policy" };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(RetryPolicy.ExponentialBackoff, config.Policy);
    }

    [Fact]
    public void ConvertRetryConfig_WithDelayStrings()
    {
        var yaml = new RetryConfigYaml
        {
            MaxRetries = 3,
            Policy = "fixed",
            InitialDelay = "1s",
            MaxDelay = "5m"
        };
        var config = YamlConfigConverter.ConvertRetryConfig(yaml);
        Assert.Equal(TimeSpan.FromSeconds(1), config.InitialDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), config.MaxDelay);
    }

    // ═══════════════════════════════════════════
    // TimeoutConfig 转换
    // ═══════════════════════════════════════════

    [Fact]
    public void ConvertTimeoutConfig_NullString_ReturnsNull()
    {
        var config = YamlConfigConverter.ConvertTimeoutConfig(null, null);
        Assert.Null(config);
    }

    [Fact]
    public void ConvertTimeoutConfig_EmptyString_ReturnsNull()
    {
        var config = YamlConfigConverter.ConvertTimeoutConfig("", null);
        Assert.Null(config);
    }

    [Fact]
    public void ConvertTimeoutConfig_WithDuration_DefaultActionIsFail()
    {
        var config = YamlConfigConverter.ConvertTimeoutConfig("30s", null);
        Assert.NotNull(config);
        Assert.Equal(TimeSpan.FromSeconds(30), config!.Duration);
        Assert.Equal(TimeoutAction.Fail, config.Action);
    }

    [Fact]
    public void ConvertTimeoutConfig_WithAction_Throw()
    {
        var config = YamlConfigConverter.ConvertTimeoutConfig("5m", "throw");
        Assert.Equal(TimeoutAction.Throw, config!.Action);
    }

    [Fact]
    public void ConvertTimeoutConfig_WithAction_Skip()
    {
        var config = YamlConfigConverter.ConvertTimeoutConfig("1h", "skip");
        Assert.Equal(TimeoutAction.Skip, config!.Action);
    }

    // ═══════════════════════════════════════════
    // ErrorPolicy 解析
    // ═══════════════════════════════════════════

    [Fact]
    public void ConvertErrorPolicy_NullString_ReturnsNull()
    {
        Assert.Null(YamlConfigConverter.ConvertErrorPolicy(null));
    }

    [Fact]
    public void ConvertErrorPolicy_EmptyString_ReturnsNull()
    {
        Assert.Null(YamlConfigConverter.ConvertErrorPolicy(""));
    }

    [Theory]
    [InlineData("failfast", ErrorPolicy.FailFast)]
    [InlineData("fail_fast", ErrorPolicy.FailFast)]
    [InlineData("FailFast", ErrorPolicy.FailFast)]
    [InlineData("continue_on_error", ErrorPolicy.ContinueOnError)]
    [InlineData("continueonerror", ErrorPolicy.ContinueOnError)]
    [InlineData("retry", ErrorPolicy.ContinueOnError)]
    [InlineData("skip_failed_branch", ErrorPolicy.SkipFailedBranch)]
    [InlineData("skipfailedbranch", ErrorPolicy.SkipFailedBranch)]
    [InlineData("skip", ErrorPolicy.SkipFailedBranch)]
    public void ConvertErrorPolicy_ValidStrings(string input, ErrorPolicy expected)
    {
        Assert.Equal(expected, YamlConfigConverter.ConvertErrorPolicy(input));
    }

    [Fact]
    public void ConvertErrorPolicy_Unknown_DefaultsToFailFast()
    {
        Assert.Equal(ErrorPolicy.FailFast, YamlConfigConverter.ConvertErrorPolicy("unknown_policy"));
    }

    // ═══════════════════════════════════════════
    // TimeSpan 简写格式
    // ═══════════════════════════════════════════

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    public void ParseTimeSpan_ShorthandFormats(string input, int expectedSeconds)
    {
        var result = YamlConfigConverter.ParseTimeSpan(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Fact]
    public void ParseTimeSpan_StandardFormat()
    {
        var result = YamlConfigConverter.ParseTimeSpan("00:10:00");
        Assert.Equal(TimeSpan.FromMinutes(10), result);
    }

    [Fact]
    public void ParseTimeSpan_InvalidFormat_Throws()
    {
        Assert.Throws<FormatException>(() => YamlConfigConverter.ParseTimeSpan("abc"));
    }

    [Fact]
    public void ParseTimeSpan_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => YamlConfigConverter.ParseTimeSpan(""));
    }
}

using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class YamlParsingTests
{
    private readonly YamlWorkflowParser _parser = new();

    [Fact]
    public void ParseSimpleWorkflow_Success()
    {
        var yaml = @"
name: test-workflow
version: '1.0'
description: 测试工作流
steps:
  - id: step-a
    type: code
    assembly: Test.Steps
    class: StepA
";

        var definition = _parser.Parse(yaml);

        Assert.Equal("test-workflow", definition.Name);
        Assert.Equal("1.0", definition.Version);
        Assert.Equal("测试工作流", definition.Description);
        Assert.Single(definition.Steps);
        Assert.Equal("step-a", definition.Steps[0].Id);
        Assert.Equal(StepType.Code, definition.Steps[0].Type);
        Assert.Equal("Test.Steps", definition.Steps[0].Assembly);
        Assert.Equal("StepA", definition.Steps[0].Class);
    }

    [Fact]
    public void ParseAgentStep_Success()
    {
        var yaml = @"
name: agent-workflow
steps:
  - id: analyze
    type: agent
    model: deepseek-chat
    system_prompt: 你是代码审查专家
    prompt: 分析以下代码
";

        var definition = _parser.Parse(yaml);

        Assert.Single(definition.Steps);
        var step = definition.Steps[0];
        Assert.Equal(StepType.Agent, step.Type);
        Assert.Equal("deepseek-chat", step.Model);
        Assert.Equal("你是代码审查专家", step.SystemPrompt);
        Assert.Equal("分析以下代码", step.Prompt);
    }

    [Fact]
    public void ParseParallelBlock_Success()
    {
        var yaml = @"
name: parallel-workflow
steps:
  - id: analysis-phase
    type: parallel
    wait_mode: all
    steps:
      - id: static-analysis
        type: agent
        model: analyzer-1
        prompt: 静态分析
      - id: security-scan
        type: agent
        model: scanner-1
        prompt: 安全扫描
";

        var definition = _parser.Parse(yaml);

        Assert.Single(definition.Steps);
        var parallelStep = definition.Steps[0];
        Assert.Equal(StepType.Parallel, parallelStep.Type);
        Assert.Equal("all", parallelStep.WaitMode);
        Assert.NotNull(parallelStep.Steps);
        Assert.Equal(2, parallelStep.Steps.Count);
        Assert.Equal("static-analysis", parallelStep.Steps[0].Id);
        Assert.Equal("security-scan", parallelStep.Steps[1].Id);
    }

    [Fact]
    public void ParseWorkflowWithDependencies_Success()
    {
        var yaml = @"
name: dependency-workflow
steps:
  - id: fetch-code
    type: code
    assembly: Steps
    class: FetchCode
  - id: analyze
    type: agent
    model: analyzer
    prompt: 分析
    depends_on:
      - fetch-code
";

        var definition = _parser.Parse(yaml);

        Assert.Equal(2, definition.Steps.Count);
        Assert.NotNull(definition.Steps[1].DependsOn);
        Assert.Contains("fetch-code", definition.Steps[1].DependsOn);
    }

    [Fact]
    public void ParseWorkflowWithRetry_Success()
    {
        var yaml = @"
name: retry-workflow
steps:
  - id: flaky-step
    type: agent
    model: analyzer
    prompt: 分析
    retry:
      max_retries: 3
      policy: exponential_backoff
      initial_delay: 1s
      backoff_factor: 2
      max_delay: 5m
";

        var definition = _parser.Parse(yaml);

        Assert.NotNull(definition.Steps[0].Retry);
        Assert.Equal(3, definition.Steps[0].Retry.MaxRetries);
        Assert.Equal("exponential_backoff", definition.Steps[0].Retry.Policy);
        Assert.Equal("1s", definition.Steps[0].Retry.InitialDelay);
        Assert.Equal(2.0, definition.Steps[0].Retry.BackoffFactor);
        Assert.Equal("5m", definition.Steps[0].Retry.MaxDelay);
    }

    [Fact]
    public void ParseWorkflowWithTimeout_Success()
    {
        var yaml = @"
name: timeout-workflow
steps:
  - id: slow-step
    type: agent
    model: analyzer
    prompt: 分析
    timeout: 5m
    timeout_action: fail
";

        var definition = _parser.Parse(yaml);

        Assert.Equal("5m", definition.Steps[0].Timeout);
        Assert.Equal("fail", definition.Steps[0].TimeoutAction);
    }

    [Fact]
    public void ParseHumanApprovalStep_Success()
    {
        var yaml = @"
name: approval-workflow
steps:
  - id: manager-approval
    type: human-approval
    notification:
      email: manager@company.com
      message: 请审批
    heartbeat_extension: 24h
";

        var definition = _parser.Parse(yaml);

        var step = definition.Steps[0];
        Assert.Equal(StepType.HumanApproval, step.Type);
        Assert.NotNull(step.Notification);
        Assert.Equal("manager@company.com", step.Notification.Email);
        Assert.Equal("请审批", step.Notification.Message);
        Assert.Equal("24h", step.HeartbeatExtension);
    }

    [Fact]
    public void ParseSubWorkflow_Success()
    {
        var yaml = @"
name: parent-workflow
steps:
  - id: security-audit
    type: workflow
    workflow_name: dependency-scan-workflow
    workflow_version: '1.0'
    input:
      repo_url: '{{steps.fetch-code.output.repo}}'
      branch: '{{steps.fetch-code.output.branch}}'
";

        var definition = _parser.Parse(yaml);

        var step = definition.Steps[0];
        Assert.Equal(StepType.Workflow, step.Type);
        Assert.Equal("dependency-scan-workflow", step.WorkflowName);
        Assert.Equal("1.0", step.WorkflowVersion);
        Assert.NotNull(step.InputMapping);
        Assert.Equal("{{steps.fetch-code.output.repo}}", step.InputMapping["repo_url"]);
    }

    [Fact]
    public void Validate_DuplicateStepIds_ThrowsValidationException()
    {
        var yaml = @"
name: duplicate-workflow
steps:
  - id: step-a
    type: code
    assembly: Steps
    class: StepA
  - id: step-a
    type: code
    assembly: Steps
    class: StepB
";

        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(yaml));
        Assert.Contains("重复的步骤ID", ex.Message);
    }

    [Fact]
    public void Validate_MissingDependency_ThrowsValidationException()
    {
        var yaml = @"
name: missing-dep-workflow
steps:
  - id: step-b
    type: agent
    model: analyzer
    prompt: 分析
    depends_on:
      - step-x
";

        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(yaml));
        Assert.Contains("依赖不存在的步骤", ex.Message);
    }

    [Fact]
    public void Validate_CircularDependency_ThrowsValidationException()
    {
        var yaml = @"
name: circular-workflow
steps:
  - id: step-a
    type: code
    assembly: Steps
    class: StepA
    depends_on: [step-b]
  - id: step-b
    type: code
    assembly: Steps
    class: StepB
    depends_on: [step-a]
";

        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(yaml));
        Assert.Contains("循环依赖", ex.Message);
    }

    [Fact]
    public void Validate_MissingRequiredFields_ThrowsValidationException()
    {
        var yaml = @"
name: invalid-workflow
steps:
  - id: step-a
    type: agent
";

        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(yaml));
        Assert.Contains("缺少model字段", ex.Message);
        Assert.Contains("缺少prompt字段", ex.Message);
    }

    [Fact]
    public void ParseFromFileAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var ex = Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await _parser.ParseFromFileAsync("nonexistent.yaml"));

        Assert.Contains("不存在", ex.Result.Message);
    }

    [Fact]
    public void Parse_EmptyYaml_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(""));
    }

    [Fact]
    public void Parse_InvalidYaml_ThrowsInvalidOperationException()
    {
        var invalidYaml = ":-invalid-yaml-structure";
        Assert.Throws<InvalidOperationException>(() => _parser.Parse(invalidYaml));
    }
}

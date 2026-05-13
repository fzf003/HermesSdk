namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>YAML 工作流定义（内嵌字符串常量）</summary>
partial class Program
{
    private const string YAML_S1_TIMEOUT = @"
name: s1-timeout-wf
version: '1.0'
steps:
  - id: timeout-demo
    type: code
    assembly: HandlerNativeConfigDemo
    class: TimeoutDemoStep
";

    private const string YAML_S2_RETRY = @"
name: s2-retry-wf
version: '1.0'
steps:
  - id: retry-demo
    type: code
    assembly: HandlerNativeConfigDemo
    class: RetryDemoStep
";

    private const string YAML_S3_OVERRIDE = @"
name: s3-override-wf
version: '1.0'
description: YAML timeout 覆盖 Fluent 配置
steps:
  - id: mixed-step
    type: code
    assembly: HandlerNativeConfigDemo
    class: MixedConfigStep
    timeout: '00:00:10'
";

    private const string YAML_S7_NO_DEFAULT = @"
name: s7-nofallback-wf
version: '1.0'
steps:
  - id: no-default-step
    type: code
    assembly: HandlerNativeConfigDemo
    class: NoDefaultStep
";

    private const string YAML_S8_BOOTSTRAPPER = @"
name: s8-bootstrapper-wf
version: '1.0'
description: 演示 IWorkflowBootstrapper 两步注册
steps:
  - id: mixed-step
    type: code
    assembly: HandlerNativeConfigDemo
    class: MixedConfigStep
    timeout: '00:00:10'
";

    private const string YAML_S9_FLUENT = @"
name: s9-fluent-wf
version: '1.0'
description: Builder Fluent API 配置演示
steps:
  - id: fluent-entry
    type: code
    assembly: HandlerNativeConfigDemo
    class: FluentEntryStep
  - id: fluent-process
    type: code
    assembly: HandlerNativeConfigDemo
    class: FluentProcessStep
    # 不设 timeout/retry → Fluent 配置生效 (10s timeout, MaxRetries=3)
  - id: fluent-validation
    type: code
    assembly: HandlerNativeConfigDemo
    class: FluentValidationStep
    # 不设 timeout → Fluent 配置生效 (15s timeout)
  - id: fluent-agent
    type: agent
    assembly: HandlerNativeConfigDemo
    class: FluentAgentDemoStep
    model: gpt-4
    prompt: YAML 配置的提示词
    timeout: '00:01:00'
    # YAML timeout=60s > Fluent timeout=30s → YAML 胜出
    # YAML prompt 被显式设置 → YAML 胜出（验证要求 Agent 步骤必须含 prompt）
";

    private const string YAML_PRIORITY_TEST = @"
name: priority-test
version: '1.0'
steps:
  - id: step-1
    type: code
    assembly: HandlerNativeConfigDemo
    class: TimeoutDemoStep
    timeout: '00:05:00'
";
}

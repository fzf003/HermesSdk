namespace HermesAgent.Sdk.WorkflowChain.Demo;

/// <summary>
/// Fluent 配置演示步骤 — 验证 Fluent API 覆盖虚属性。
/// Handler 虚属性：Timeout=500ms, Retry=MaxRetries=1
/// Fluent 配置：Timeout=10s, Retry=MaxRetries=3
/// → Fluent 胜出
/// </summary>
internal sealed class FluentProcessStep : CodeStepHandler
{
    public override string StepId => "fluent-process";
    public static int ExecutionCount;

    /// <summary>虚属性 500ms — 步骤实际需 2s，若生效会超时</summary>
    public override string Timeout => "00:00:00.500";

    /// <summary>虚属性 MaxRetries=1（共 1 次尝试）— 若生效则无法完成重试</summary>
    public override RetryConfigYaml? Retry => new() { MaxRetries = 1, Policy = RetryPolicy.Immediate };

    public static void ResetCount() => ExecutionCount = 0;

    public override async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        ExecutionCount++;
        Console.WriteLine($"  🔄 FluentProcessStep: attempt #{ExecutionCount}");

        // 模拟工作（2s — 超出虚属性 500ms 但小于 Fluent 10s）
        await Task.Delay(2000, ct);

        // 前 2 次抛异常触发重试（需 Fluent MaxRetries=3 才能在第 3 次成功）
        if (ExecutionCount < 3)
            throw new TimeoutException("模拟失败以触发重试");

        Console.WriteLine("  ✅ FluentProcessStep: 第3次执行成功 (Fluent Timeout+Retry 生效)");
        return Sequential("fluent-validation");
    }
}

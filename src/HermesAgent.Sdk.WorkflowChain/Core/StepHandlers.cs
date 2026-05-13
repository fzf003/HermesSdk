namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// Agent 通信模式。
/// </summary>
public enum AgentCommunicationMode
{
    None,
    /// <summary>Webhook 回调模式 — Agent 完成后 HTTP POST 通知</summary>
    Webhook,

    /// <summary>RunClient SSE 模式 — 通过 SSE 订阅事件</summary>
    RunClient,
}

/// <summary>
/// Agent 步骤 — 通过 Webhook 或 RunClient SSE 调用 Hermes Agent 处理。
/// 异步执行，发请求后不等，回调/事件回来时调度器推进。
/// </summary>
public abstract class AgentStepHandler : StepHandlerBase
{
    /// <summary>Agent 通信模式。默认 Webhook，可重写为 RunClient。</summary>
    public virtual AgentCommunicationMode Mode => AgentCommunicationMode.None;

    /// <summary>Hermes config.yaml 中配置的路由名称（Webhook 模式使用）</summary>
    public virtual string RouteName => "";

    /// <summary>事件类型（Webhook 模式使用）</summary>
    public virtual string EventType => "";

    /// <summary>Agent 的 System Prompt（可选）</summary>
    public virtual string? SystemPrompt => null;
    public virtual string? Prompt => null;

    /// <summary>Agent 的 User Prompt 模板（引用 context 中的前序输出）</summary>
    public abstract string BuildPrompt(WorkflowContext context);

    /// <summary>RunClient 模式下的 Run 选项（可选）</summary>
    public virtual RunOptions? RunOptions => null;
}

/// <summary>
/// 本地代码步骤 — 直接执行 .NET 代码。
/// 同步执行，不需要 Agent 参与。
/// </summary>
public abstract class CodeStepHandler : StepHandlerBase
{
    // ExecuteAsync 由子类实现
}

/// <summary>
/// 延迟步骤 — 等待一段时间后继续。
/// </summary>
public class DelayStepHandler : StepHandlerBase
{
    private readonly TimeSpan _delay;
    private readonly string _nextStepId;

    public override string StepId { get; }
    public TimeSpan DelayDuration => _delay;

    public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
        => throw new InvalidOperationException("DelayStep 不在 ExecuteAsync 中执行，Engine 通过 DelayDuration/NextStepId 内部处理");

    /// <summary>创建一个延迟步骤</summary>
    /// <param name="stepId">步骤唯一标识</param>
    /// <param name="delay">延迟时间</param>
    /// <param name="nextStepId">超时后推进到的下一步 ID</param>
    public DelayStepHandler(string stepId, TimeSpan delay, string nextStepId)
    {
        StepId = stepId;
        _delay = delay;
        _nextStepId = nextStepId;
    }

    /// <summary>执行延迟并返回下一步结果（Engine 内部调用）</summary>
    internal async Task<StepResult> ExecuteDelayAsync(CancellationToken ct)
    {
        await Task.Delay(_delay, ct);
        return Sequential(_nextStepId);
    }
}

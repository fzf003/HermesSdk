namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// DSL 工作流步骤构建器 — 在 <c>Workflow.Build()</c> 中使用。
/// 支持三种注册方式：
/// <list type="bullet">
///   <item><c>AddCodeStep(id, fn)</c> — 内联 Lambda 定义 Code 步骤</item>
///   <item><c>AddAgentStep(id, cfg)</c> — 内联 Lambda 定义 Agent 步骤</item>
///   <item><c>AddCodeStep&lt;T&gt;()</c> / <c>AddAgentStep&lt;T&gt;()</c> — 引用现有 Handler 类</item>
/// </list>
/// </summary>
public interface IStepBuilder
{
    /// <summary>内联定义 Code 步骤。Lambda 签名：<c>(WorkflowContext, CancellationToken) → Task&lt;StepResult&gt;</c></summary>
    /// <param name="stepId">步骤唯一标识</param>
    /// <param name="execute">执行函数，返回 StepResult 控制流转</param>
    DslCodeStepBuilder AddCodeStep(string stepId, Func<WorkflowContext, CancellationToken, Task<StepResult>> execute);

    /// <summary>内联定义 Agent 步骤。Lambda 返回 <see cref="AgentConfig"/> 配置 prompt。</summary>
    /// <param name="stepId">步骤唯一标识</param>
    /// <param name="configure">返回 AgentConfig 的工厂函数（可选）</param>
    DslAgentStepBuilder AddAgentStep(string stepId, Func<WorkflowContext, AgentConfig>? configure = null);

    /// <summary>引用现有 CodeStepHandler 类（与 <c>WorkflowStepBuilder.AddCodeStep&lt;T&gt;()</c> 等价）。</summary>
    void AddCodeStep<T>() where T : CodeStepHandler;

    /// <summary>引用现有 AgentStepHandler 类（与 <c>WorkflowStepBuilder.AddAgentStep&lt;T&gt;()</c> 等价）。</summary>
    void AddAgentStep<T>() where T : AgentStepHandler;
}

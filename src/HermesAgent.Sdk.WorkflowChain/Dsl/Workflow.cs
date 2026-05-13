namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// 工作流抽象基类 — 通过 <c>Build(IStepBuilder)</c> 内联定义步骤。
/// 由 <c>Register&lt;T&gt;()</c> 注册到引擎，与 <c>AddWorkflow</c> Fluent API 等价。
/// </summary>
public abstract class Workflow
{
    /// <summary>工作流名称，全局唯一标识。</summary>
    public abstract string Name { get; }

    /// <summary>工作流唯一 ID。默认自动生成 GUID，可重写为固定值。</summary>
    public virtual string Id => Guid.NewGuid().ToString("N");

    /// <summary>构建工作流步骤定义。由 <c>Register&lt;T&gt;</c> 内部调用。</summary>
    protected internal abstract void Build(IStepBuilder builder);

    /// <summary>工作流描述（可选）。</summary>
    public virtual string? Description => null;

    /// <summary>语义化版本号（可选，默认 "1.0"）。</summary>
    public virtual string Version => "1.0";
}

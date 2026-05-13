namespace HermesAgent.Sdk.WorkflowChain.Dsl;

/// <summary>
/// DSL Workflow 注册扩展方法。
/// 提供 <c>Register&lt;T&gt;()</c> 在 <see cref="WorkflowChainBuilder"/> 上注册类继承式工作流定义。
/// </summary>
public static class WorkflowChainDslExtensions
{
    /// <summary>
    /// 注册一个 DSL 工作流。创建 <typeparamref name="TWorkflow"/> 实例，
    /// 调用其 <c>Build(IStepBuilder)</c> 方法构建步骤定义，
    /// 最终产出 <see cref="WorkflowDefinition"/> 并注册到 Builder。
    /// </summary>
    /// <typeparam name="TWorkflow">工作流类型（必须有无参构造函数）</typeparam>
    /// <param name="builder">工作流链式构建器</param>
    /// <exception cref="InvalidOperationException">
    /// 工作流名称重复、步骤定义不合法时抛出。
    /// </exception>
    public static void Register<TWorkflow>(this WorkflowChainBuilder builder)
        where TWorkflow : Workflow, new()
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        var workflow = new TWorkflow();

        if (string.IsNullOrWhiteSpace(workflow.Name))
            throw new InvalidOperationException(
                $"工作流 {typeof(TWorkflow).Name}.Name 返回空值");

        if (builder.HasWorkflowDefinition(workflow.Name))
            throw new InvalidOperationException(
                $"工作流 \"{workflow.Name}\" 已通过 AddWorkflow 注册，不允许重复注册");

        var stepBuilder = new DslStepBuilder(builder);
        workflow.Build(stepBuilder);
        stepBuilder.BuildDefinition(workflow.Id, workflow.Name, workflow.Version, workflow.Description);
    }
}

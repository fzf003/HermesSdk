namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// WorkflowEngine 扩展方法 — 提供按工作流名称启动等便利功能。
/// </summary>
public static class WorkflowEngineExtensions
{
    /// <summary>
    /// 按工作流名称启动工作流。
    /// 自动查找入口步骤（优先选无 <c>DependsOn</c> 的步骤），
    /// 自动关联 <see cref="WorkflowDefinition.Id"/> 到 <see cref="WorkflowContext.InstanceId"/>。
    /// </summary>
    /// <param name="engine">工作流引擎实例</param>
    /// <param name="workflowName">工作流名称（已注册到 <paramref name="registry"/> 的 key）</param>
    /// <param name="context">工作流上下文。<see cref="WorkflowContext.InstanceId"/> 为 null 时自动生成</param>
    /// <param name="registry">工作流注册表（通过 DI 解析）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工作流实例 ID</returns>
    public static async Task<string> StartWorkflowAsync(
        this WorkflowEngine engine,
        string workflowName,
        WorkflowContext context,
        WorkflowRegistry registry,
        CancellationToken ct = default)
    {
        var def = registry.Get(workflowName);

        // 入口步骤：优先找无 DependsOn 的步骤（DAG 入口），兜底取第一个步骤
        var entryStep = def.Steps.FirstOrDefault(s => s.DependsOn == null || s.DependsOn.Count == 0)
                    ?? def.Steps.FirstOrDefault()
                    ?? throw new ArgumentException($"工作流 \"{workflowName}\" 没有步骤定义");

        // InstanceId 与 WorkflowDefinition.Id 对齐（引擎实例Key = 工作流Id = YAML id）
        context.InstanceId = def.Id;

        return await engine.StartAsync(entryStep.Id, context, ct, workflowName);
    }
}

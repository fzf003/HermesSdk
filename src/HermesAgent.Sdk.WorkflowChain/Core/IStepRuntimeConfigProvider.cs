namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 步骤运行时配置源抽象接口。
/// 负责合并来自多种来源的步骤策略配置（YAML / Fluent API / Handler 虚属性 / 引擎默认值），
/// 产生运行时最终生效的 <see cref="MergedStepRuntimeConfig"/>。
/// </summary>
public interface IStepRuntimeConfigProvider
{
    /// <summary>
    /// 获取步骤的运行时最终配置。
    /// </summary>
    /// <param name="handler">步骤处理器实例</param>
    /// <param name="workflowDef">工作流定义（可能为 null）</param>
    /// <param name="stepDef">步骤定义（可能为 null，如通过 Fluent API 注册的步骤）</param>
    /// <returns>合并后的运行时配置</returns>
    MergedStepRuntimeConfig GetConfig(
        IStepHandler handler,
        WorkflowDefinition? workflowDef,
        StepDefinition? stepDef);
}

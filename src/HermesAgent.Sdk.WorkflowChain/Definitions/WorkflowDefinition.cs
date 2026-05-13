using YamlDotNet.Serialization;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>工作流定义 —— 描述一个完整的工作流，包含唯一标识、名称、版本、步骤拓扑等。</summary>
/// <remarks>
/// 可通过 YAML 解析器（<see cref="YamlWorkflowParser"/>）反序列化，
/// 或通过代码直接构造后注册到 <see cref="WorkflowRegistry"/>。
/// 注册前建议调用 <see cref="Validate"/> 检查定义的合法性。
/// </remarks>
public class WorkflowDefinition
{
    /// <summary>工作流唯一标识（自动生成 GUID），在 WorkflowRegistry 中稳定引用。</summary>
    [YamlMember(Alias = "id")] public string Id { get; set; } = "";

    /// <summary>工作流名称，全局唯一标识。不能为空。</summary>
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";

    /// <summary>语义化版本号（如 "1.0.0", "2.0.0-beta"）。默认值：1.0。</summary>
    [YamlMember(Alias = "version")] public string Version { get; set; } = "1.0";

    /// <summary>工作流描述信息。</summary>
    [YamlMember(Alias = "description")] public string? Description { get; set; }

    /// <summary>自定义元数据键值对，框架不处理，供外部系统使用。</summary>
    [YamlMember(Alias = "metadata")] public Dictionary<string, object?> Metadata { get; set; } = new();

    /// <summary>步骤定义列表。必须包含至少一个步骤。</summary>
    [YamlMember(Alias = "steps")] public List<StepDefinition> Steps { get; set; } = new();

    /// <summary>验证工作流定义的合法性。返回所有验证错误列表，空列表表示定义合法。</summary>
    /// <returns>错误信息列表。若返回空列表表示验证通过。</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("工作流名称(name)不能为空");
        if (Steps == null || Steps.Count == 0) { errors.Add("工作流必须包含至少一个步骤(steps)"); return errors; }

        var stepIds = new HashSet<string>();
        foreach (var step in Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id)) { errors.Add("步骤ID(id)不能为空"); continue; }
            if (!stepIds.Add(step.Id)) errors.Add($"重复的步骤ID: {step.Id}");
        }

        var allStepIds = Steps.Select(s => s.Id).ToHashSet();
        foreach (var step in Steps.Where(s => s.DependsOn != null && s.DependsOn.Any()))
        {
            foreach (var dep in step.DependsOn!)
            {
                if (!allStepIds.Contains(dep)) errors.Add($"步骤 {step.Id} 依赖不存在的步骤: {dep}");
            }
        }

        if (HasCircularDependency(Steps)) errors.Add("检测到循环依赖");

        foreach (var step in Steps) ValidateStep(step, errors);
        return errors;
    }

    /// <summary>校验单个步骤的字段完整性（按类型检查必填字段）。</summary>
    private void ValidateStep(StepDefinition step, List<string> errors)
    {
        switch (step.Type)
        {
            case StepType.Agent:
                if (string.IsNullOrWhiteSpace(step.Model)) errors.Add($"Agent步骤 {step.Id} 缺少model字段");
                if (string.IsNullOrWhiteSpace(step.Prompt)) errors.Add($"Agent步骤 {step.Id} 缺少prompt字段");
                break;
            case StepType.Code:
                if (string.IsNullOrWhiteSpace(step.Assembly)) errors.Add($"Code步骤 {step.Id} 缺少assembly字段");
                if (string.IsNullOrWhiteSpace(step.Class)) errors.Add($"Code步骤 {step.Id} 缺少class字段");
                break;
            case StepType.Delay:
                if (string.IsNullOrWhiteSpace(step.Duration)) errors.Add($"Delay步骤 {step.Id} 缺少duration字段");
                if (string.IsNullOrWhiteSpace(step.NextStepId)) errors.Add($"Delay步骤 {step.Id} 缺少next_step_id字段");
                break;
            case StepType.HumanApproval:
                if (step.Notification == null) errors.Add($"HumanApproval步骤 {step.Id} 缺少notification配置");
                break;
            case StepType.Workflow:
                if (string.IsNullOrWhiteSpace(step.WorkflowName)) errors.Add($"Workflow步骤 {step.Id} 缺少workflow_name字段");
                break;
            case StepType.Sequential:
            case StepType.Parallel:
                if (step.Steps == null || step.Steps.Count == 0) errors.Add($"{step.Type}块 {step.Id} 必须包含子步骤");
                else foreach (var subStep in step.Steps) ValidateStep(subStep, errors);
                break;
        }
    }

    /// <summary>检测步骤依赖是否存在循环依赖（DFS + 三色标记）。</summary>
    private bool HasCircularDependency(List<StepDefinition> steps)
    {
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        bool Dfs(string stepId)
        {
            if (inStack.Contains(stepId)) return true;
            if (visited.Contains(stepId)) return false;
            visited.Add(stepId); inStack.Add(stepId);
            var step = steps.FirstOrDefault(s => s.Id == stepId);
            if (step?.DependsOn != null) foreach (var dep in step.DependsOn) if (Dfs(dep)) return true;
            inStack.Remove(stepId); return false;
        }
        return steps.Any(s => Dfs(s.Id));
    }
}

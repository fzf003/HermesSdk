using YamlDotNet.Serialization;

namespace HermesAgent.Sdk.WorkflowChain;

public class WorkflowDefinition
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "version")] public string Version { get; set; } = "1.0";
    [YamlMember(Alias = "description")] public string? Description { get; set; }
    [YamlMember(Alias = "metadata")] public Dictionary<string, object?> Metadata { get; set; } = new();
    [YamlMember(Alias = "steps")] public List<StepDefinition> Steps { get; set; } = new();

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

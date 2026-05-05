using System.Collections.Concurrent;
using HermesAgent.Sdk.WorkflowChain.Internal;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流注册表 - 管理工作流定义的注册和查询。
/// </summary>
public class WorkflowRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows = new();
    private readonly ConcurrentDictionary<string, string> _defaultVersions = new();

    /// <summary>
    /// 注册工作流定义。
    /// </summary>
    /// <param name="definition">工作流定义</param>
    public void Register(WorkflowDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var key = GetWorkflowKey(definition.Name, definition.Version);
        _workflows[key] = definition;

        // 如果没有设置默认版本,或这是更新的版本,则更新默认版本
        if (!_defaultVersions.TryGetValue(definition.Name, out var currentDefault) ||
            CompareVersions(definition.Version, currentDefault) > 0)
        {
            _defaultVersions[definition.Name] = definition.Version;
        }
    }

    /// <summary>
    /// 获取工作流定义(使用默认版本)。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <returns>工作流定义</returns>
    /// <exception cref="KeyNotFoundException">工作流未注册时抛出</exception>
    public WorkflowDefinition Get(string name)
    {
        if (!_defaultVersions.TryGetValue(name, out var version))
            throw new KeyNotFoundException($"工作流 {name} 未注册");

        return GetByVersion(name, version);
    }

    /// <summary>
    /// 获取指定版本的工作流定义。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号</param>
    /// <returns>工作流定义</returns>
    /// <exception cref="KeyNotFoundException">工作流或版本不存在时抛出</exception>
    public WorkflowDefinition GetByVersion(string name, string version)
    {
        var key = GetWorkflowKey(name, version);
        if (!_workflows.TryGetValue(key, out var definition))
            throw new KeyNotFoundException($"工作流 {name} 版本 {version} 不存在");

        return definition;
    }

    /// <summary>
    /// 检查工作流是否已注册。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <returns>是否已注册</returns>
    public bool IsRegistered(string name)
    {
        return _defaultVersions.ContainsKey(name);
    }

    /// <summary>
    /// 获取所有已注册的工作流名称。
    /// </summary>
    /// <returns>工作流名称列表</returns>
    public IEnumerable<string> GetRegisteredNames()
    {
        return _defaultVersions.Keys;
    }

    /// <summary>
    /// 获取工作流的所有已注册版本。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <returns>版本号列表(降序)</returns>
    public IEnumerable<string> GetVersions(string name)
    {
        return _workflows.Keys
            .Where(k => k.StartsWith($"{name}:"))
            .Select(k => k.Substring(k.IndexOf(':') + 1))
            .OrderByDescending(v => {
                try {
                    var parts = ParseVersionParts(v);
                    return (parts.Major, parts.Minor, parts.Patch, parts.PreRelease ?? "");
                } catch {
                    return (0, 0, 0, v);
                }
            });
    }

    /// <summary>
    /// 设置默认版本。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号</param>
    public void SetDefaultVersion(string name, string version)
    {
        var key = GetWorkflowKey(name, version);
        if (!_workflows.ContainsKey(key))
            throw new KeyNotFoundException($"工作流 {name} 版本 {version} 不存在");

        _defaultVersions[name] = version;
    }

    private static string GetWorkflowKey(string name, string version)
    {
        return $"{name}:{version}";
    }

    private static int CompareVersions(string v1, string v2)
    {
        // 实现语义化版本比较
        try
        {
            var parts1 = ParseVersionParts(v1);
            var parts2 = ParseVersionParts(v2);

            // 比较主版本号
            if (parts1.Major != parts2.Major)
                return parts1.Major > parts2.Major ? 1 : -1;

            // 比较次版本号
            if (parts1.Minor != parts2.Minor)
                return parts1.Minor > parts2.Minor ? 1 : -1;

            // 比较修订号
            if (parts1.Patch != parts2.Patch)
                return parts1.Patch > parts2.Patch ? 1 : -1;

            // 预发布版本处理：有预发布标签的版本较小
            if (string.IsNullOrEmpty(parts1.PreRelease) && !string.IsNullOrEmpty(parts2.PreRelease))
                return 1;
            if (!string.IsNullOrEmpty(parts1.PreRelease) && string.IsNullOrEmpty(parts2.PreRelease))
                return -1;
            if (!string.IsNullOrEmpty(parts1.PreRelease) && !string.IsNullOrEmpty(parts2.PreRelease))
                return string.Compare(parts1.PreRelease, parts2.PreRelease, StringComparison.Ordinal);

            return 0;
        }
        catch
        {
            // 如果解析失败，回退到字符串比较
            return string.Compare(v1, v2, StringComparison.Ordinal);
        }
    }

    internal static (int Major, int Minor, int Patch, string? PreRelease) ParseVersionParts(string version) => SemanticVersionHelper.ParseVersionParts(version);
}

using System.Collections.Concurrent;
using HermesAgent.Sdk.WorkflowChain.Internal;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流版本管理器 - 提供语义化版本管理、回滚、标签等功能。
/// </summary>
public class WorkflowVersionManager
{
    private readonly WorkflowRegistry _registry;
    private readonly ConcurrentDictionary<string, List<VersionHistoryEntry>> _versionHistory = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _versionTags = new();

    /// <summary>
    /// 创建工作流版本管理器。
    /// </summary>
    /// <param name="registry">工作流注册表</param>
    public WorkflowVersionManager(WorkflowRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// 注册新版本并记录历史。
    /// </summary>
    /// <param name="definition">工作流定义</param>
    /// <param name="changeLog">变更说明</param>
    public void RegisterVersion(WorkflowDefinition definition, string? changeLog = null)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var previousVersion = GetLatestVersion(definition.Name);
        _registry.Register(definition);

        // 记录版本历史
        var history = _versionHistory.GetOrAdd(definition.Name, _ => new List<VersionHistoryEntry>());
        lock (history)
        {
            history.Add(new VersionHistoryEntry
            {
                Version = definition.Version,
                RegisteredAt = DateTime.UtcNow,
                ChangeLog = changeLog,
                PreviousVersion = previousVersion
            });
        }
    }

    /// <summary>
    /// 比较两个语义化版本号。
    /// </summary>
    /// <param name="v1">版本1</param>
    /// <param name="v2">版本2</param>
    /// <returns>-1: v1小于v2, 0: v1等于v2, 1: v1大于v2</returns>
    public int CompareSemanticVersions(string v1, string v2)
    {
        if (string.IsNullOrEmpty(v1))
            throw new ArgumentException("版本号不能为空", nameof(v1));
        if (string.IsNullOrEmpty(v2))
            throw new ArgumentException("版本号不能为空", nameof(v2));

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

        // 比较预发布标签（简化处理：有预发布标签的版本较小）
        if (string.IsNullOrEmpty(parts1.PreRelease) && !string.IsNullOrEmpty(parts2.PreRelease))
            return 1;
        if (!string.IsNullOrEmpty(parts1.PreRelease) && string.IsNullOrEmpty(parts2.PreRelease))
            return -1;
        if (!string.IsNullOrEmpty(parts1.PreRelease) && !string.IsNullOrEmpty(parts2.PreRelease))
            return string.Compare(parts1.PreRelease, parts2.PreRelease, StringComparison.Ordinal);

        return 0;
    }

    /// <summary>
    /// 递增补丁版本号。
    /// </summary>
    /// <param name="version">当前版本</param>
    /// <returns>新版本号</returns>
    public string IncrementPatchVersion(string version)
    {
        var parts = ParseVersionParts(version);
        return $"{parts.Major}.{parts.Minor}.{parts.Patch + 1}";
    }

    /// <summary>
    /// 递增次版本号（重置补丁号为0）。
    /// </summary>
    /// <param name="version">当前版本</param>
    /// <returns>新版本号</returns>
    public string IncrementMinorVersion(string version)
    {
        var parts = ParseVersionParts(version);
        return $"{parts.Major}.{parts.Minor + 1}.0";
    }

    /// <summary>
    /// 递增主版本号（重置次版本和补丁号为0）。
    /// </summary>
    /// <param name="version">当前版本</param>
    /// <returns>新版本号</returns>
    public string IncrementMajorVersion(string version)
    {
        var parts = ParseVersionParts(version);
        return $"{parts.Major + 1}.0.0";
    }

    /// <summary>
    /// 回滚到指定版本。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">目标版本</param>
    /// <returns>回滚的工作流定义</returns>
    public WorkflowDefinition RollbackToVersion(string name, string version)
    {
        var definition = _registry.GetByVersion(name, version);
        _registry.SetDefaultVersion(name, version);

        // 记录回滚操作
        var history = _versionHistory.GetOrAdd(name, _ => new List<VersionHistoryEntry>());
        lock (history)
        {
            history.Add(new VersionHistoryEntry
            {
                Version = version,
                RegisteredAt = DateTime.UtcNow,
                ChangeLog = $"Rollback to version {version}",
                PreviousVersion = GetLatestVersion(name),
                IsRollback = true
            });
        }

        return definition;
    }

    /// <summary>
    /// 为版本添加标签。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="version">版本号</param>
    /// <param name="tag">标签名</param>
    public void AddTag(string name, string version, string tag)
    {
        // 验证版本存在
        _ = _registry.GetByVersion(name, version);

        var tags = _versionTags.GetOrAdd($"{name}:{version}", _ => new HashSet<string>());
        lock (tags)
        {
            tags.Add(tag);
        }
    }

    /// <summary>
    /// 根据标签获取版本。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="tag">标签名</param>
    /// <returns>带标签的版本列表</returns>
    public IEnumerable<string> GetVersionsByTag(string name, string tag)
    {
        var results = new List<string>();
        foreach (var kvp in _versionTags)
        {
            if (kvp.Key.StartsWith($"{name}:") && kvp.Value.Contains(tag))
            {
                var version = kvp.Key.Substring(kvp.Key.IndexOf(':') + 1);
                results.Add(version);
            }
        }
        return results.OrderByDescending(v => v);
    }

    /// <summary>
    /// 获取版本历史记录。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <returns>版本历史列表（按时间倒序）</returns>
    public IEnumerable<VersionHistoryEntry> GetVersionHistory(string name)
    {
        if (!_versionHistory.TryGetValue(name, out var history))
            return Enumerable.Empty<VersionHistoryEntry>();

        lock (history)
        {
            return history.OrderByDescending(h => h.RegisteredAt).ToList();
        }
    }

    /// <summary>
    /// 获取最新版本号。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <returns>最新版本号，如果未注册则返回 null</returns>
    public string? GetLatestVersion(string name)
    {
        try
        {
            var versions = _registry.GetVersions(name).ToList();
            if (versions.Count == 0)
                return null;

            // 使用语义化版本比较找到最新版本
            string latest = versions[0];
            for (int i = 1; i < versions.Count; i++)
            {
                if (CompareSemanticVersions(versions[i], latest) > 0)
                    latest = versions[i];
            }
            return latest;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    internal static (int Major, int Minor, int Patch, string? PreRelease) ParseVersionParts(string version) => SemanticVersionHelper.ParseVersionParts(version);

    /// <summary>
    /// 版本历史条目。
    /// </summary>
    public class VersionHistoryEntry
    {
        /// <summary>
        /// 版本号。
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 注册时间。
        /// </summary>
        public DateTime RegisteredAt { get; set; }

        /// <summary>
        /// 变更说明。
        /// </summary>
        public string? ChangeLog { get; set; }

        /// <summary>
        /// 前一个版本。
        /// </summary>
        public string? PreviousVersion { get; set; }

        /// <summary>
        /// 是否为回滚操作。
        /// </summary>
        public bool IsRollback { get; set; }
    }
}

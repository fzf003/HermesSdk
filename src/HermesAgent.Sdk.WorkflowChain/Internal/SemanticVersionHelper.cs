namespace HermesAgent.Sdk.WorkflowChain.Internal;

/// <summary>
/// 语义化版本解析辅助类 — 供 WorkflowRegistry 和 WorkflowVersionManager 共享。
/// </summary>
internal static class SemanticVersionHelper
{
    /// <summary>
    /// 解析语义化版本字符串为 (Major, Minor, Patch, PreRelease) 元组。
    /// 支持格式: major.minor.patch[-prerelease]
    /// </summary>
    public static (int Major, int Minor, int Patch, string? PreRelease) ParseVersionParts(string version)
    {
        var preRelease = default(string);
        var versionPart = version;

        var dashIndex = version.IndexOf('-');
        if (dashIndex > 0)
        {
            preRelease = version.Substring(dashIndex + 1);
            versionPart = version.Substring(0, dashIndex);
        }

        var parts = versionPart.Split('.');
        if (parts.Length < 2 || parts.Length > 3)
            throw new ArgumentException($"无效的版本号格式: {version}", nameof(version));

        int major = int.Parse(parts[0]);
        int minor = int.Parse(parts[1]);
        int patch = parts.Length == 3 ? int.Parse(parts[2]) : 0;

        return (major, minor, patch, preRelease);
    }
}

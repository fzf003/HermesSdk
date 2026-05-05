using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class WorkflowVersionManagerTests
{
    private static WorkflowRegistry CreateRegistry() => new();
    private static WorkflowVersionManager CreateManager(WorkflowRegistry registry) => new(registry);

    private static WorkflowDefinition CreateDefinition(string name, string version) => new()
    {
        Name = name,
        Version = version,
        Steps = [new() { Id = "s1", Type = StepType.Code, Assembly = "A", Class = "C" }]
    };

    // ═══════════════════════════════════════════
    // 注册版本
    // ═══════════════════════════════════════════

    [Fact]
    public void RegisterVersion_AddsToRegistry()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);
        var def = CreateDefinition("test-wf", "1.0.0");

        manager.RegisterVersion(def, "initial");

        Assert.True(registry.IsRegistered("test-wf"));
        Assert.Equal("1.0.0", registry.Get("test-wf").Version);
    }

    [Fact]
    public void RegisterVersion_RecordsHistory()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        manager.RegisterVersion(CreateDefinition("wf", "1.0.0"), "initial");
        manager.RegisterVersion(CreateDefinition("wf", "1.1.0"), "update");

        var history = manager.GetVersionHistory("wf").ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("1.1.0", history[0].Version); // 按时间倒序
        Assert.Equal("1.0.0", history[1].Version);
        Assert.Equal("update", history[0].ChangeLog);
    }

    [Fact]
    public void RegisterVersion_NullDefinition_Throws()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        Assert.Throws<ArgumentNullException>(() => manager.RegisterVersion(null!));
    }

    // ═══════════════════════════════════════════
    // 回滚
    // ═══════════════════════════════════════════

    [Fact]
    public void RollbackToVersion_SetsDefaultVersion()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        manager.RegisterVersion(CreateDefinition("wf", "1.0.0"));
        manager.RegisterVersion(CreateDefinition("wf", "2.0.0"));

        var rolledBack = manager.RollbackToVersion("wf", "1.0.0");

        Assert.Equal("1.0.0", rolledBack.Version);
        // 验证历史记录中包含回滚
        var history = manager.GetVersionHistory("wf").ToList();
        Assert.Contains(history, h => h.IsRollback && h.Version == "1.0.0");
    }

    // ═══════════════════════════════════════════
    // 标签
    // ═══════════════════════════════════════════

    [Fact]
    public void AddTag_AndGetVersionsByTag()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        manager.RegisterVersion(CreateDefinition("wf", "1.0.0"));
        manager.RegisterVersion(CreateDefinition("wf", "2.0.0"));

        manager.AddTag("wf", "1.0.0", "stable");
        manager.AddTag("wf", "2.0.0", "beta");

        var stableVersions = manager.GetVersionsByTag("wf", "stable").ToList();
        var betaVersions = manager.GetVersionsByTag("wf", "beta").ToList();

        Assert.Single(stableVersions);
        Assert.Equal("1.0.0", stableVersions[0]);
        Assert.Single(betaVersions);
        Assert.Equal("2.0.0", betaVersions[0]);
    }

    // ═══════════════════════════════════════════
    // 语义化版本比较
    // ═══════════════════════════════════════════

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.1.0", "1.0.1", 1)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    public void CompareSemanticVersions_CorrectComparison(string v1, string v2, int expected)
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        var result = manager.CompareSemanticVersions(v1, v2);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompareSemanticVersions_NullVersion_Throws()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        Assert.Throws<ArgumentException>(() => manager.CompareSemanticVersions("", "1.0.0"));
        Assert.Throws<ArgumentException>(() => manager.CompareSemanticVersions("1.0.0", null!));
    }

    // ═══════════════════════════════════════════
    // 版本递增
    // ═══════════════════════════════════════════

    [Fact]
    public void IncrementPatchVersion_IncrementsPatch()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);
        Assert.Equal("1.2.4", manager.IncrementPatchVersion("1.2.3"));
    }

    [Fact]
    public void IncrementMinorVersion_IncrementsMinor_ResetsPatch()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);
        Assert.Equal("1.3.0", manager.IncrementMinorVersion("1.2.3"));
    }

    [Fact]
    public void IncrementMajorVersion_IncrementsMajor_ResetsMinorAndPatch()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);
        Assert.Equal("2.0.0", manager.IncrementMajorVersion("1.2.3"));
    }

    // ═══════════════════════════════════════════
    // GetLatestVersion
    // ═══════════════════════════════════════════

    [Fact]
    public void GetLatestVersion_ReturnsHighestSemanticVersion()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        manager.RegisterVersion(CreateDefinition("wf", "1.0.0"));
        manager.RegisterVersion(CreateDefinition("wf", "2.1.0"));
        manager.RegisterVersion(CreateDefinition("wf", "2.0.10"));

        var latest = manager.GetLatestVersion("wf");
        Assert.Equal("2.1.0", latest);
    }

    [Fact]
    public void GetLatestVersion_NoVersions_ReturnsNull()
    {
        var registry = CreateRegistry();
        var manager = CreateManager(registry);

        Assert.Null(manager.GetLatestVersion("nonexistent"));
    }
}

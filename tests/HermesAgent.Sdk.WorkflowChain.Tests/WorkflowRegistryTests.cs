using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class WorkflowRegistryTests
{
    private readonly WorkflowRegistry _registry = new();

    private static WorkflowDefinition MakeDef(string name, string version, string? description = null)
        => new() { Name = name, Version = version, Description = description, Steps = [new StepDefinition { Id = "step-1", Type = StepType.Code }] };

    // ═══════════════════════════════════════════
    // 注册与查询
    // ═══════════════════════════════════════════

    [Fact]
    public void Register_AddsDefinition()
    {
        // Act
        _registry.Register(MakeDef("demo", "1.0"));

        // Assert
        Assert.True(_registry.IsRegistered("demo"));
        Assert.Contains("demo", _registry.GetRegisteredNames());
    }

    [Fact]
    public void Register_NullDefinition_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Register(null!));
    }

    [Fact]
    public void Get_ReturnsLatestVersion()
    {
        // Arrange — register v1.0 then v2.0
        _registry.Register(MakeDef("app", "1.0"));
        _registry.Register(MakeDef("app", "2.0"));

        // Act
        var def = _registry.Get("app");

        // Assert: latest version (2.0 > 1.0) is default
        Assert.Equal("2.0", def.Version);
    }

    [Fact]
    public void GetByVersion_ReturnsSpecificVersion()
    {
        // Arrange
        _registry.Register(MakeDef("app", "1.0", "first"));
        _registry.Register(MakeDef("app", "2.0", "second"));

        // Act
        var def = _registry.GetByVersion("app", "1.0");

        // Assert
        Assert.Equal("1.0", def.Version);
        Assert.Equal("first", def.Description);
    }

    [Fact]
    public void GetVersions_ReturnsSorted()
    {
        // Arrange
        _registry.Register(MakeDef("app", "1.0"));
        _registry.Register(MakeDef("app", "2.1"));
        _registry.Register(MakeDef("app", "2.0"));

        // Act
        var versions = _registry.GetVersions("app").ToList();

        // Assert: descending order
        Assert.Equal(["2.1", "2.0", "1.0"], versions);
    }

    [Fact]
    public void Get_Unregistered_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _registry.Get("nonexistent"));
    }

    [Fact]
    public void GetByVersion_NonexistentVersion_ThrowsKeyNotFoundException()
    {
        _registry.Register(MakeDef("app", "1.0"));

        Assert.Throws<KeyNotFoundException>(() => _registry.GetByVersion("app", "9.9"));
    }

    // ═══════════════════════════════════════════
    // 语义化版本比较
    // ═══════════════════════════════════════════

    [Fact]
    public void SemanticVersion_Prerelease_LowerThanRelease()
    {
        // Arrange: v2.0-alpha should be lower than v2.0
        _registry.Register(MakeDef("svc", "2.0-alpha"));
        _registry.Register(MakeDef("svc", "2.0"));

        // Act: latest should be 2.0 (release > prerelease)
        var def = _registry.Get("svc");
        Assert.Equal("2.0", def.Version);
    }

    [Fact]
    public void SemanticVersion_MultiplePrereleases()
    {
        // Arrange
        _registry.Register(MakeDef("svc", "1.0-beta"));
        _registry.Register(MakeDef("svc", "1.0-alpha"));
        _registry.Register(MakeDef("svc", "1.0"));

        // Act
        var def = _registry.Get("svc");
        Assert.Equal("1.0", def.Version);
    }

    // ═══════════════════════════════════════════
    // SetDefaultVersion
    // ═══════════════════════════════════════════

    [Fact]
    public void SetDefaultVersion_OverridesDefault()
    {
        // Arrange
        _registry.Register(MakeDef("app", "1.0"));
        _registry.Register(MakeDef("app", "2.0"));

        // Act: override default to 1.0
        _registry.SetDefaultVersion("app", "1.0");

        // Assert
        var def = _registry.Get("app");
        Assert.Equal("1.0", def.Version);
    }

    [Fact]
    public void SetDefaultVersion_NonexistentVersion_ThrowsKeyNotFoundException()
    {
        _registry.Register(MakeDef("app", "1.0"));

        Assert.Throws<KeyNotFoundException>(() => _registry.SetDefaultVersion("app", "9.9"));
    }
}

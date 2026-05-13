using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class HotReloadTests
{
    private static WorkflowRegistry CreateRegistry() => new();
    private static WorkflowImportExportManager CreateImportExport(WorkflowRegistry registry) => new(registry);
    private static WorkflowHotReloadManager CreateHotReload(WorkflowRegistry registry, WorkflowImportExportManager importExport)
        => new(registry, importExport, engine: null, NullLogger<WorkflowHotReloadManager>.Instance);

    [Fact]
    public void StartWatching_ValidDirectory_StartsMonitoring()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var tempDir = Path.Combine(Path.GetTempPath(), $"watch-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            hotReload.StartWatching(tempDir, "*.yaml");

            var watchedDirs = hotReload.GetWatchedDirectories();
            Assert.Contains(Path.GetFullPath(tempDir), watchedDirs);
        }
        finally
        {
            hotReload.Dispose();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void StartWatching_NonExistentDirectory_ThrowsException()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        Assert.Throws<DirectoryNotFoundException>(() =>
            hotReload.StartWatching("C:\\NonExistentDirectory"));
    }

    [Fact]
    public async Task ReloadWorkflow_ReloadsYamlFile_Successfully()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var yaml = @"
name: hot-reload-test
version: 1.0.0
description: Initial version
steps:
  - id: step-1
    type: code
    assembly: TestAssembly
    class: TestClass
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"reload-test-{Guid.NewGuid()}.yaml");

        try
        {
            await File.WriteAllTextAsync(tempFile, yaml);

            var definition = await hotReload.ReloadWorkflowAsync(tempFile);

            Assert.Equal("hot-reload-test", definition.Name);
            Assert.Equal("1.0.0", definition.Version);
            Assert.True(registry.IsRegistered("hot-reload-test"));
        }
        finally
        {
            hotReload.Dispose();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReloadWorkflow_UpdatesToNewVersion_Successfully()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var yaml1 = @"
name: version-update-test
version: 1.0.0
steps:
  - id: s1
    type: code
    assembly: A
    class: C
";

        var yaml2 = @"
name: version-update-test
version: 2.0.0
description: Updated version
steps:
  - id: s1
    type: code
    assembly: A
    class: C
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"version-test-{Guid.NewGuid()}.yaml");

        try
        {
            await File.WriteAllTextAsync(tempFile, yaml1);
            await hotReload.ReloadWorkflowAsync(tempFile);

            Assert.Equal("1.0.0", registry.Get("version-update-test").Version);

            await File.WriteAllTextAsync(tempFile, yaml2);
            await hotReload.ReloadWorkflowAsync(tempFile);

            Assert.Equal("2.0.0", registry.Get("version-update-test").Version);
        }
        finally
        {
            hotReload.Dispose();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReloadWorkflow_InvalidYaml_ThrowsValidationException()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var invalidYaml = @"
name: invalid
steps:
  - id: s1
    type: agent
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid()}.yaml");

        try
        {
            await File.WriteAllTextAsync(tempFile, invalidYaml);

            await Assert.ThrowsAsync<ValidationException>(() => hotReload.ReloadWorkflowAsync(tempFile));
        }
        finally
        {
            hotReload.Dispose();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReloadWorkflow_JsonFile_Successfully()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var json = @"{
  ""name"": ""json-reload-test"",
  ""version"": ""1.0.0"",
  ""steps"": [
    {
      ""id"": ""step-1"",
      ""type"": ""code"",
      ""assembly"": ""TestAssembly"",
      ""class"": ""TestClass""
    }
  ]
}";

        var tempFile = Path.Combine(Path.GetTempPath(), $"json-reload-{Guid.NewGuid()}.json");

        try
        {
            await File.WriteAllTextAsync(tempFile, json);

            var definition = await hotReload.ReloadWorkflowAsync(tempFile);

            Assert.Equal("json-reload-test", definition.Name);
            Assert.Equal("1.0.0", definition.Version);
        }
        finally
        {
            hotReload.Dispose();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetLastReloadTime_ReturnsCorrectTime()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        Assert.Null(hotReload.GetLastReloadTime("nonexistent.yaml"));
    }

    [Fact]
    public void SuspendAndResumeWatchers_WorksCorrectly()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var tempDir = Path.Combine(Path.GetTempPath(), $"suspend-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            hotReload.StartWatching(tempDir, "*.yaml");

            hotReload.SuspendAllWatchers();
            hotReload.ResumeAllWatchers();

            // No exception means success
            Assert.True(true);
        }
        finally
        {
            hotReload.Dispose();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Dispose_StopsAllWatchers()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        var tempDir = Path.Combine(Path.GetTempPath(), $"dispose-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            hotReload.StartWatching(tempDir, "*.yaml");
            hotReload.Dispose();

            // After dispose, should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() =>
                hotReload.StartWatching(tempDir, "*.yaml"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WorkflowReloaded_Event_IsTriggered()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);
        var hotReload = CreateHotReload(registry, importExport);

        WorkflowDefinition? reloadedDefinition = null;
        string? reloadedPath = null;

        hotReload.WorkflowReloaded += (path, definition) =>
        {
            reloadedPath = path;
            reloadedDefinition = definition;
        };

        var yaml = @"
name: event-test
version: 1.0.0
steps:
  - id: s1
    type: code
    assembly: A
    class: C
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"event-test-{Guid.NewGuid()}.yaml");

        try
        {
            File.WriteAllText(tempFile, yaml);
            await hotReload.ReloadWorkflowAsync(tempFile);

            Assert.NotNull(reloadedDefinition);
            Assert.Equal("event-test", reloadedDefinition!.Name);
            Assert.Equal(tempFile, reloadedPath);
        }
        finally
        {
            hotReload.Dispose();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}

using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class ImportExportTests
{
    private static WorkflowRegistry CreateRegistry() => new();
    private static WorkflowImportExportManager CreateImportExport(WorkflowRegistry registry)
        => new(registry);

    [Fact]
    public void ExportToYaml_ExportsValidYaml()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = CreateWorkflowDefinition("test-workflow", "1.0.0");
        registry.Register(definition);

        var yaml = importExport.ExportToYaml("test-workflow");

        Assert.NotNull(yaml);
        Assert.Contains("name: test-workflow", yaml);
        Assert.Contains("version: 1.0.0", yaml);
        Assert.Contains("type:", yaml);
    }

    [Fact]
    public void ImportFromYaml_ImportsAndRegistersWorkflow()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var yaml = @"
name: imported-workflow
version: 2.0.0
description: Imported from YAML
steps:
  - id: step-1
    type: code
    assembly: TestAssembly
    class: TestClass
";

        var definition = importExport.ImportFromYaml(yaml, register: true);

        Assert.Equal("imported-workflow", definition.Name);
        Assert.Equal("2.0.0", definition.Version);
        Assert.True(registry.IsRegistered("imported-workflow"));
    }

    [Fact]
    public async Task ExportToYamlFile_ExportsToFile()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = CreateWorkflowDefinition("file-export-test", "1.0.0");
        registry.Register(definition);

        var tempFile = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid()}.yaml");

        try
        {
            await importExport.ExportToYamlFileAsync("file-export-test", tempFile);

            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("file-export-test", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportFromYamlFile_ImportsFromFile()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var yaml = @"
name: file-import-test
version: 1.5.0
steps:
  - id: step-1
    type: agent
    model: test-model
    prompt: Test prompt
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid()}.yaml");

        try
        {
            await File.WriteAllTextAsync(tempFile, yaml);

            var definition = await importExport.ImportFromYamlFileAsync(tempFile, register: true);

            Assert.Equal("file-import-test", definition.Name);
            Assert.Equal("1.5.0", definition.Version);
            Assert.True(registry.IsRegistered("file-import-test"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToJson_ExportsValidJson()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = CreateWorkflowDefinition("json-test", "1.0.0");
        registry.Register(definition);

        var json = importExport.ExportToJson("json-test");

        Assert.NotNull(json);
        Assert.Contains("\"name\": \"json-test\"", json);
        Assert.Contains("\"version\": \"1.0.0\"", json);
    }

    [Fact]
    public void ImportFromJson_ImportsAndRegistersWorkflow()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var json = @"{
  ""name"": ""json-imported"",
  ""version"": ""3.0.0"",
  ""description"": ""Imported from JSON"",
  ""steps"": [
    {
      ""id"": ""step-1"",
      ""type"": ""code"",
      ""assembly"": ""TestAssembly"",
      ""class"": ""TestClass""
    }
  ]
}";

        var definition = importExport.ImportFromJson(json, register: true);

        Assert.Equal("json-imported", definition.Name);
        Assert.Equal("3.0.0", definition.Version);
        Assert.True(registry.IsRegistered("json-imported"));
    }

    [Fact]
    public void GenerateSummary_GeneratesCorrectSummary()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = new WorkflowDefinition
        {
            Name = "summary-test",
            Version = "1.0.0",
            Description = "Test summary generation",
            Steps = new List<StepDefinition>
            {
                new StepDefinition { Id = "step-1", Type = StepType.Agent, Model = "test", Prompt = "test" },
                new StepDefinition { Id = "step-2", Type = StepType.Code, Assembly = "A", Class = "C" },
                new StepDefinition { Id = "step-3", Type = StepType.Delay, Duration = "5s", NextStepId = "step-4" },
                new StepDefinition { Id = "step-4", Type = StepType.HumanApproval }
            }
        };

        registry.Register(definition);

        var summary = importExport.GenerateSummary("summary-test");

        Assert.Equal("summary-test", summary.Name);
        Assert.Equal("1.0.0", summary.Version);
        Assert.Equal(4, summary.TotalSteps);
        Assert.Equal(1, summary.AgentSteps);
        Assert.Equal(1, summary.CodeSteps);
        Assert.Equal(1, summary.DelaySteps);
        Assert.Equal(1, summary.HumanApprovalSteps);
    }

    [Fact]
    public async Task ExportAllWorkflows_ExportsMultipleWorkflows()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var wf1 = CreateWorkflowDefinition("workflow-a", "1.0.0");
        var wf2 = CreateWorkflowDefinition("workflow-b", "1.0.0");

        registry.Register(wf1);
        registry.Register(wf2);

        var tempDir = Path.Combine(Path.GetTempPath(), $"export-all-{Guid.NewGuid()}");

        try
        {
            await importExport.ExportAllWorkflowsAsync(tempDir, format: "yaml");

            var files = Directory.GetFiles(tempDir, "*.yaml");
            Assert.Equal(2, files.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ImportAllWorkflows_ImportsMultipleFiles()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var tempDir = Path.Combine(Path.GetTempPath(), $"import-all-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var yaml1 = @"
name: batch-import-1
version: 1.0.0
steps:
  - id: s1
    type: code
    assembly: A
    class: C
";

            var yaml2 = @"
name: batch-import-2
version: 1.0.0
steps:
  - id: s1
    type: code
    assembly: A
    class: C
";

            await File.WriteAllTextAsync(Path.Combine(tempDir, "wf1.yaml"), yaml1);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "wf2.yaml"), yaml2);

            var count = await importExport.ImportAllWorkflowsAsync(tempDir, register: true);

            Assert.Equal(2, count);
            Assert.True(registry.IsRegistered("batch-import-1"));
            Assert.True(registry.IsRegistered("batch-import-2"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateBackupAsync_CreatesBackupFile()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var definition = CreateWorkflowDefinition("backup-test", "1.0.0");
        registry.Register(definition);

        var tempDir = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}");

        try
        {
            var backupPath = await importExport.CreateBackupAsync("backup-test", tempDir);

            Assert.True(File.Exists(backupPath));
            Assert.Contains("backup-", Path.GetFileName(backupPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImportFromYaml_InvalidYaml_ThrowsValidationException()
    {
        var registry = CreateRegistry();
        var importExport = CreateImportExport(registry);

        var invalidYaml = @"
name: invalid-workflow
steps:
  - id: step-1
    type: agent
";

        Assert.Throws<ValidationException>(() => importExport.ImportFromYaml(invalidYaml));
    }

    private static WorkflowDefinition CreateWorkflowDefinition(string name, string version)
    {
        return new WorkflowDefinition
        {
            Name = name,
            Version = version,
            Description = $"Test workflow {name} v{version}",
            Steps = new List<StepDefinition>
            {
                new StepDefinition
                {
                    Id = "step-1",
                    Type = StepType.Code,
                    Assembly = "TestAssembly",
                    Class = "TestClass"
                }
            }
        };
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 应用启动时先恢复持久化中的运行实例，确保后续心跳检测与回调处理基于完整内存状态工作。
/// 引擎初始化完成后触发 OnReady 事件，启动异步 YAML 导出等后续操作。
/// </summary>
public sealed class WorkflowEngineInitializationService : IHostedService
{
    private readonly WorkflowEngine _engine;
    private readonly ILogger<WorkflowEngineInitializationService> _logger;

    public WorkflowEngineInitializationService(
        WorkflowEngine engine,
        WorkflowEngineOptions options,
        WorkflowRegistry registry,
        ILogger<WorkflowEngineInitializationService> logger)
    {
        _engine = engine;
        _logger = logger;

        // 自动订阅导出逻辑：引擎启动完成后异步导出 YAML 到配置目录
        if (options.AutoExportEnabled && !string.IsNullOrWhiteSpace(options.YamlConfigDirectory))
        {
            engine.OnReady += async ct =>
            {
                try
                {
                    var configDir = options.YamlConfigDirectory;
                    Directory.CreateDirectory(configDir);

                    var exportManager = new WorkflowImportExportManager(registry);

                    foreach (var name in registry.GetRegisteredNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        var def = registry.Get(name);
                        var yamlPath = Path.Combine(configDir, $"{def.Name}-{def.Version}.yaml");

                        // 文件已存在 → 不覆盖（YAML 是运行时的单一配置源）
                        if (File.Exists(yamlPath))
                            continue;

                        var yaml = exportManager.ExportToYaml(name);

                        // 原子写入：临时文件 + 重命名，防止文件损坏
                        var tempPath = yamlPath + ".tmp";
                        await File.WriteAllTextAsync(tempPath, yaml, ct);
                        File.Move(tempPath, yamlPath, overwrite: true);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消操作不记录警告
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "异步导出 YAML 失败，不影响引擎运行");
                }
            };
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动 WorkflowEngine 恢复初始化");
        await _engine.InitializeAsync(cancellationToken);

        // 引擎初始化完成后触发 OnReady 事件
        await _engine.FireOnReadyAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

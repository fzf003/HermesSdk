using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 应用启动时先恢复持久化中的运行实例，确保后续心跳检测与回调处理基于完整内存状态工作。
/// </summary>
public sealed class WorkflowEngineInitializationService : IHostedService
{
    private readonly WorkflowEngine _engine;
    private readonly ILogger<WorkflowEngineInitializationService> _logger;

    public WorkflowEngineInitializationService(
        WorkflowEngine engine,
        ILogger<WorkflowEngineInitializationService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动 WorkflowEngine 恢复初始化");
        await _engine.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

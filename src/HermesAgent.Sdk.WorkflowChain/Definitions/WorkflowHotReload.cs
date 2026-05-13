using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流热加载管理器 - 监控文件系统变化并自动重新加载工作流定义。
/// </summary>
public class WorkflowHotReloadManager : IDisposable
{
    private readonly WorkflowRegistry _registry;
    private readonly WorkflowImportExportManager _importExport;
    private readonly WorkflowEngine? _engine;
    private readonly ILogger<WorkflowHotReloadManager>? _logger;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastReloadTime = new();
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private volatile bool _disposed;

    /// <summary>
    /// 工作流文件变更事件。
    /// </summary>
    public event Action<string, string>? WorkflowFileChanged;

    /// <summary>
    /// 工作流重新加载完成事件。
    /// </summary>
    public event Action<string, WorkflowDefinition>? WorkflowReloaded;

    /// <summary>
    /// 创建热加载管理器实例。
    /// </summary>
    /// <param name="registry">工作流注册表</param>
    /// <param name="importExport">导入导出管理器</param>
    /// <param name="engine">工作流引擎(可选，用于热加载时同步步骤配置)</param>
    /// <param name="logger">日志记录器(可选)</param>
    public WorkflowHotReloadManager(
        WorkflowRegistry registry,
        WorkflowImportExportManager importExport,
        WorkflowEngine? engine = null,
        ILogger<WorkflowHotReloadManager>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _importExport = importExport ?? throw new ArgumentNullException(nameof(importExport));
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// 开始监控指定目录中的工作流文件。
    /// </summary>
    /// <param name="directoryPath">要监控的目录路径</param>
    /// <param name="filter">文件过滤器(如 "*.yaml", "*.json")</param>
    /// <param name="includeSubdirectories">是否包含子目录(默认false)</param>
    public void StartWatching(string directoryPath, string filter = "*.*", bool includeSubdirectories = false)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkflowHotReloadManager));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"目录不存在: {directoryPath}");

        var watcher = new FileSystemWatcher
        {
            Path = Path.GetFullPath(directoryPath),
            Filter = filter,
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileDeleted;
        watcher.Renamed += OnFileRenamed;
        watcher.Error += OnWatcherError;

        watcher.EnableRaisingEvents = true;

        var key = GetWatcherKey(directoryPath, filter);
        _watchers[key] = watcher;

        _logger?.LogInformation("开始监控目录: {Directory}, 过滤器: {Filter}", directoryPath, filter);
    }

    /// <summary>
    /// 停止监控指定目录。
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="filter">文件过滤器</param>
    public void StopWatching(string directoryPath, string filter = "*.*")
    {
        var key = GetWatcherKey(directoryPath, filter);

        if (_watchers.TryRemove(key, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger?.LogInformation("停止监控目录: {Directory}", directoryPath);
        }
    }

    /// <summary>
    /// 手动重新加载指定文件的工作流定义。
    /// </summary>
    /// <param name="filePath">工作流文件路径</param>
    /// <returns>重新加载的工作流定义</returns>
    public async Task<WorkflowDefinition> ReloadWorkflowAsync(string filePath)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkflowHotReloadManager));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}");

        // 使用 SemaphoreSlim 实现纯异步锁定，避免死锁
        await _reloadSemaphore.WaitAsync();
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            WorkflowDefinition definition;

            if (extension == ".yaml" || extension == ".yml")
            {
                definition = await _importExport.ImportFromYamlFileAsync(filePath, register: false);
            }
            else if (extension == ".json")
            {
                definition = await _importExport.ImportFromJsonFileAsync(filePath, register: false);
            }
            else
            {
                throw new InvalidOperationException($"不支持的文件格式: {extension}");
            }

            // 验证并注册新版本
            var errors = definition.Validate();
            if (errors.Any())
            {
                throw new InvalidOperationException(
                    $"工作流定义验证失败:\n{string.Join("\n", errors.Select(e => $"- {e}"))}");
            }

            // 如果已存在同名工作流,则注册为新版本
            if (_registry.IsRegistered(definition.Name))
            {
                var existingVersion = _registry.Get(definition.Name).Version;
                _logger?.LogInformation(
                    "重新加载工作流: {Name}, 旧版本: {OldVersion}, 新版本: {NewVersion}",
                    definition.Name, existingVersion, definition.Version);
            }

            _registry.Register(definition);
            _engine?.ReplaceStepDefinitions(definition.Name, definition.Steps);
            _lastReloadTime[filePath] = DateTime.UtcNow;

            WorkflowReloaded?.Invoke(filePath, definition);

            return definition;
        }
        finally
        {
            _reloadSemaphore.Release();
        }
    }

    /// <summary>
    /// 获取工作流的上次重新加载时间。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>上次重新加载时间,未重新加载则返回null</returns>
    public DateTime? GetLastReloadTime(string filePath)
    {
        return _lastReloadTime.TryGetValue(filePath, out var time) ? time : (DateTime?)null;
    }

    /// <summary>
    /// 获取所有正在监控的目录。
    /// </summary>
    /// <returns>监控目录列表</returns>
    public List<string> GetWatchedDirectories()
    {
        return _watchers.Keys.Select(k => k.Split('|')[0]).Distinct().ToList();
    }

    /// <summary>
    /// 暂停所有文件监控器的事件引发。
    /// </summary>
    public void SuspendAllWatchers()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
        }
        _logger?.LogDebug("已暂停所有文件监控器");
    }

    /// <summary>
    /// 恢复所有文件监控器的事件引发。
    /// </summary>
    public void ResumeAllWatchers()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = true;
        }
        _logger?.LogDebug("已恢复所有文件监控器");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _reloadSemaphore.Dispose();

        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _logger?.LogInformation("热加载管理器已释放");
    }

    // =================================================================
    // 私有方法
    // =================================================================

    private static string GetWatcherKey(string directoryPath, string filter)
    {
        return $"{Path.GetFullPath(directoryPath)}|{filter}";
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            // 防抖:忽略短时间内的重复事件
            if (_lastReloadTime.TryGetValue(e.FullPath, out var lastTime) &&
                (DateTime.UtcNow - lastTime).TotalSeconds < 1)
            {
                return;
            }

            _logger?.LogDebug("检测到文件变更: {FilePath}", e.FullPath);
            WorkflowFileChanged?.Invoke(e.ChangeType.ToString(), e.FullPath);

            // 自动重新加载 — 使用 Task.Run 确保不在 FileSystemWatcher 线程上执行异步操作
            await Task.Run(async () =>
            {
                await Task.Delay(500); // 等待文件写入完成
                await ReloadWorkflowAsync(e.FullPath);
            });
        }
        catch (Exception ex)
        {
            // 顶层 try-catch 完全色裹，防止 async void 未观察异常崩溃进程
            _logger?.LogWarning(ex, "自动重新加载工作流失败: {FilePath}", e.FullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
            return;

        _logger?.LogInformation("工作流文件已删除: {FilePath}", e.FullPath);
        _lastReloadTime.TryRemove(e.FullPath, out _);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed)
            return;

        _logger?.LogInformation("工作流文件已重命名: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        _lastReloadTime.TryRemove(e.OldFullPath, out _);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger?.LogError(e.GetException(), "文件监控器发生错误");
    }
}

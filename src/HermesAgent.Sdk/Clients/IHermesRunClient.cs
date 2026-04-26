using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 运行客户端接口，用于执行和监控 AI 运行任务。
/// 使用场景：需要执行复杂的 AI 任务、监控运行状态或处理异步运行结果时使用。
/// 支持启动运行、订阅事件、等待完成和带日志的运行。
/// </summary>
public interface IHermesRunClient : IDisposable
{
    /// <summary>
    /// 启动运行任务。
    /// 使用场景：异步启动 AI 运行任务，返回运行 ID 用于后续监控。
    /// </summary>
    /// <param name="prompt">运行提示或指令。</param>
    /// <param name="options">运行选项，如模型、工具等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行 ID。</returns>
    Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 订阅运行事件。
    /// 使用场景：实时监控运行进度和状态变化，如构建事件驱动的界面。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步可枚举的运行事件。</returns>
    IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// 启动并等待运行完成。
    /// 使用场景：同步执行运行任务并等待结果，适用于简单的一次性任务。
    /// </summary>
    /// <param name="prompt">运行提示。</param>
    /// <param name="options">运行选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行结果。</returns>
    Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 带日志的运行。
    /// 使用场景：执行运行任务并自动记录日志，适用于调试或监控场景。
    /// </summary>
    /// <param name="prompt">运行提示。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务完成时返回。</returns>
    Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default);
}

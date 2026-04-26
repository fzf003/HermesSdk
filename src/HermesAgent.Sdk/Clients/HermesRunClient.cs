using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 运行客户端实现，用于执行和监控 AI 运行任务。
/// 使用场景：应用程序需要执行复杂的 AI 任务、监控运行状态或处理异步运行结果。
/// 支持启动运行、订阅事件、等待完成和带日志的运行。
/// </summary>
public class HermesRunClient : IHermesRunClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 初始化 HermesRunClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    public HermesRunClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 启动运行任务实现。
    /// 使用场景：异步启动 AI 运行任务，返回运行 ID 用于后续监控。
    /// </summary>
    /// <param name="prompt">运行提示或指令。</param>
    /// <param name="options">运行选项，如模型、工具等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行 ID。</returns>
    public async Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
    {
        var request = new RunRequest
        {
            Prompt = prompt,
            Model = options?.Model ?? "default",
            Skills = options?.Skills,
            MaxIterations = options?.MaxIterations,
            Metadata = options?.Metadata
        };

        var response = await _httpClient.PostAsJsonAsync("/v1/runs", request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunStartResponse>(_jsonOptions, ct);
        return result?.RunId ?? throw new InvalidOperationException("Invalid run start response");
    }

    /// <summary>
    /// 订阅运行事件实现。
    /// 使用场景：实时监控运行进度和状态变化，如构建事件驱动的界面。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步可枚举的运行事件。</returns>
    public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/v1/runs/{runId}/events", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line[6..];
            if (data == "[DONE]")
                yield break;

            var evt = JsonSerializer.Deserialize<RunEvent>(data, _jsonOptions);
            if (evt is not null)
                yield return evt;
        }
    }

    /// <summary>
    /// 启动并等待运行完成实现。
    /// 使用场景：同步执行运行任务并等待结果，适用于简单的一次性任务。
    /// </summary>
    /// <param name="prompt">运行提示。</param>
    /// <param name="options">运行选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
    {
        var runId = await StartAsync(prompt, options, ct);
        var result = new RunResult
        {
            RunId = runId,
            Status = "pending"
        };

        var output = result.Output;
        var status = result.Status;
        var errorMessage = result.ErrorMessage;

        await foreach (var evt in SubscribeEventsAsync(runId, ct))
        {
            if (evt.Type == "completion")
            {
                if (evt.Data != null && evt.Data.TryGetValue("content", out var content))
                    output = content?.ToString();
                status = "completed";
            }
            else if (evt.Type == "error")
            {
                if (evt.Data != null && evt.Data.TryGetValue("message", out var message))
                    errorMessage = message?.ToString();
                status = "failed";
            }
        }

        return result with { Output = output, Status = status, ErrorMessage = errorMessage };

    }

    /// <summary>
    /// 带日志的运行实现。
    /// 使用场景：执行运行任务并自动记录日志，适用于调试或监控场景。
    /// </summary>
    /// <param name="prompt">运行提示。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务完成时返回。</returns>
    public async Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
    {
        var runId = await StartAsync(prompt, null, ct);
        logger?.LogInformation("🚀 运行已启动 (run {RunId})", runId);
        await foreach (var evt in SubscribeEventsAsync(runId, ct))
        {
            logger?.LogInformation("event {EventType}: {Data}", evt.Type, evt.Data);
        }
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

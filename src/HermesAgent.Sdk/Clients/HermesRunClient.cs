using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 运行客户端实现，用于执行和监控 AI 运行任务。
/// 使用场景：应用程序需要执行复杂的 AI 任务、监控运行状态或处理异步运行结果。
/// 支持启动运行、查询状态、订阅事件、中断运行、审批和等待完成。
///
/// <para>对应端点参考：</para>
/// <list type="table">
///   <item><term>POST /v1/runs</term><description>StartAsync — 创建异步 Run</description></item>
///   <item><term>GET /v1/runs/{id}</term><description>GetRunStatusAsync — 轮询状态</description></item>
///   <item><term>GET /v1/runs/{id}/events</term><description>SubscribeEventsAsync — SSE 事件流</description></item>
///   <item><term>POST /v1/runs/{id}/stop</term><description>StopRunAsync — 中断 Run</description></item>
///   <item><term>POST /v1/runs/{id}/approval</term><description>ApproveRunAsync — 审批工具调用</description></item>
/// </list>
/// </summary>
public class HermesRunClient : IHermesRunClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
    private readonly ILogger<HermesRunClient> _logger;
    /// <summary>
    /// 初始化 HermesRunClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    /// <param name="logger">用于记录日志的 ILogger 实例。</param>
    public HermesRunClient(HttpClient httpClient, ILogger<HermesRunClient> logger)
    {
        _httpClient = httpClient;

        _logger = logger;
    }

    /// <summary>
    /// 启动运行任务实现。
    /// 使用场景：异步启动 AI 运行任务，返回运行 ID 用于后续监控。
    /// </summary>
    /// <param name="prompt">运行提示或指令。</param>
    /// <param name="options">运行选项，如模型、工具等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行 ID。</returns>
    public async Task<RunStartResponse> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
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
        return result;
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
            this._logger.LogDebug(data);
            var evt = JsonSerializer.Deserialize<RunEvent>(data, _jsonOptions);
            if (evt is not null)
            {
                yield return evt;
            }
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
        RunResult result = default;
        try
        {
            var output = result.Output;
            var status = result.Status;
            var errorMessage = result.ErrorMessage;

            await RunWithLoggingAsync(prompt, (@event, taskid) =>
            {

                if (@event.Type == "run.completion")
                {
                    if (@event.Data != null && @event.Data.TryGetValue("content", out var content))
                        output = content?.ToString();
                    status = "completed";
                }
                else if (@event.Type == "run.error")
                {
                    if (@event.Data != null && @event.Data.TryGetValue("message", out var message))
                        errorMessage = message?.ToString();
                    status = "failed";
                }

            }, logger: _logger, ct: ct);

            result = result with { Output = output, Status = status, ErrorMessage = errorMessage };
        }
        catch (Exception ex)
        {
            result = new RunResult { Status = "failed", ErrorMessage = ex.Message };
        }

        return result;

    }

    /// <summary>
    /// 带日志的运行实现。
    /// 使用场景：执行运行任务并自动记录日志，适用于调试或监控场景。
    /// </summary>
    /// <param name="prompt">运行提示。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务完成时返回。</returns>
    public async Task RunWithLoggingAsync(string prompt, Action<RunEvent, string> eventaction = null, ILogger? logger = null, CancellationToken ct = default)
    {
        var runStart = await StartAsync(prompt, null, ct);
        logger?.LogInformation("🚀 运行已启动 (run {RunId})", runStart.RunId);
        await foreach (var evt in SubscribeEventsAsync(runStart.RunId, ct))
        {
            logger?.LogInformation("event {EventType}: {Data}", evt.Type, evt.Data);
            if (eventaction is not null)
            {
                eventaction(evt, runStart.RunId);
            }
        }

    }

    // ──────────────────────────────────────────────────────────
    //  新增方法 — 补全 /v1/runs 端点能力
    //  设计依据: API Server 文档 (hermes-api-server-docs.md)
    //  变更: run-client-complete
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 查询运行状态。
    /// 对应 GET /v1/runs/{run_id}。
    ///
    /// <para>设计说明：</para>
    /// <list type="bullet">
    ///   <item>Server 端 Run 数据存储在内存中（非持久化），终态 1 小时 TTL 后返回 404</item>
    ///   <item>404 返回 null（不抛异常），其他 HTTP 错误抛异常</item>
    ///   <item>终态（completed/failed/cancelled）的 <c>output</c> 和 <c>usage</c> 会被填充</item>
    /// </list>
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>运行状态响应；若 Run 不存在或已过期返回 null。</returns>
    public async Task<RunStatusResponse?> GetRunStatusAsync(string runId, CancellationToken ct = default)
    {
        // GET /v1/runs/{run_id} — 文档: "获取 Run 的当前状态（内存中，非持久化）"
        var response = await _httpClient.GetAsync($"/v1/runs/{runId}", ct);

        // Run 不存在或已过期（1h TTL 后 Server 返回 404）
        // 设计决策: 返回 null 而非抛异常，调用方无需 try-catch
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Run {RunId} not found (may have expired after 1h TTL)", runId);
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunStatusResponse>(_jsonOptions, ct);
    }

    /// <summary>
    /// 中断正在执行的 Run。
    /// 对应 POST /v1/runs/{run_id}/stop。
    ///
    /// <para>设计说明：</para>
    /// <list type="bullet">
    ///   <item>请求体可以为空（Server 不校验 body）</item>
    ///   <item>返回 status: "stopping"（非同步停止，Server 异步处理）</item>
    ///   <item>中断流程：agent.interrupt() → task.cancel() → 等待 5s → 返回</item>
    /// </list>
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>中断响应（status: "stopping"）。</returns>
    public async Task<StopRunResponse> StopRunAsync(string runId, CancellationToken ct = default)
    {
        // POST /v1/runs/{run_id}/stop
        // 文档: "中断正在执行的 Agent。请求体可以为空（{} 或空）"
        // 使用 null content 发送空 body POST
        var response = await _httpClient.PostAsync($"/v1/runs/{runId}/stop", content: null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StopRunResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Invalid stop run response");
    }

    /// <summary>
    /// 审批 Run 的挂起审批请求。
    /// 对应 POST /v1/runs/{run_id}/approval。
    ///
    /// <para>设计说明：</para>
    /// <list type="bullet">
    ///   <item><c>choice</c>：once（仅当前）、session（本次会话自动批准）、always（永久批准）、deny（拒绝）</item>
    ///   <item><c>all: true</c> 批量解析所有挂起的审批，false 仅处理最早的一个</item>
    ///   <item>无挂起审批时 Server 返回 409 → 抛异常</item>
    ///   <item>无效 choice 值 Server 返回 400 → 抛异常</item>
    /// </list>
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="approval">审批请求（choice + all）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批响应（含 resolved 计数）。</returns>
    public async Task<ApprovalResponse> ApproveRunAsync(string runId, ApprovalRequest approval, CancellationToken ct = default)
    {
        // POST /v1/runs/{run_id}/approval
        // 文档: "当 Agent 触发安全审批时，通过此接口批准或拒绝工具调用"
        // 使用 PostAsJsonAsync 自动序列化 ApprovalRequest → JSON body
        var response = await _httpClient.PostAsJsonAsync($"/v1/runs/{runId}/approval", approval, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Invalid approval response");
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

using System.Net.Http.Json;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 作业客户端实现，用于管理定时或周期性 AI 作业。
/// 使用场景：应用程序需要调度和管理重复执行的 AI 任务，如定时报告生成、数据处理任务。
/// 支持作业的创建、更新、暂停、恢复和手动执行操作。
/// </summary>
public class HermesJobClient : IHermesJobClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 HermesJobClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    public HermesJobClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 列出所有作业实现。
    /// 使用场景：查看系统中所有已定义的作业列表。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业摘要列表。</returns>
    public async Task<List<JobSummary>> ListAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("/api/jobs", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobSummary>>(cancellationToken: ct)
            ?? new List<JobSummary>();
    }

    /// <summary>
    /// 获取作业详情实现。
    /// 使用场景：根据作业 ID 获取详细的作业信息和配置。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情，如果不存在则为 null。</returns>
    public async Task<JobDetail?> GetAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/jobs/{jobId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<JobDetail>(cancellationToken: ct);
    }

    /// <summary>
    /// 创建新作业实现。
    /// 使用场景：定义新的定时或周期性 AI 任务。
    /// </summary>
    /// <param name="request">作业创建请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建的作业详情。</returns>
    public async Task<JobDetail> CreateAsync(JobCreateRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/jobs", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDetail>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid job detail");
    }

    /// <summary>
    /// 更新作业实现。
    /// 使用场景：修改现有作业的配置，如调度时间、参数等。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="request">作业更新请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的作业详情。</returns>
    public async Task<JobDetail> UpdateAsync(string jobId, JobUpdateRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PatchAsync($"/api/jobs/{jobId}", JsonContent.Create(request), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDetail>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid job detail");
    }

    /// <summary>
    /// 删除作业实现。
    /// 使用场景：移除不再需要的作业定义。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
    public async Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/jobs/{jobId}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 暂停作业实现。
    /// 使用场景：临时停止作业执行，稍后可以恢复。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>暂停后的作业详情。</returns>
    public async Task<JobDetail> PauseAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/jobs/{jobId}/pause", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDetail>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid job detail");
    }

    /// <summary>
    /// 恢复作业实现。
    /// 使用场景：重新启动之前暂停的作业。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>恢复后的作业详情。</returns>
    public async Task<JobDetail> ResumeAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/jobs/{jobId}/resume", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDetail>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid job detail");
    }

    /// <summary>
    /// 立即执行作业实现。
    /// 使用场景：手动触发作业执行，绕过调度时间。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>执行结果。</returns>
    public async Task<JobRunResult> RunNowAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/jobs/{jobId}/run", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobRunResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid job run result");
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

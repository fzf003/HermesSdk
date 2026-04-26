namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 作业客户端接口，用于管理定时或周期性 AI 作业。
/// 使用场景：需要创建、调度和管理重复执行的 AI 任务时使用，如定时报告生成、数据处理任务。
/// 支持作业的生命周期管理，包括创建、更新、暂停、恢复和手动执行。
/// </summary>
public interface IHermesJobClient : IDisposable
{
    /// <summary>
    /// 列出所有作业。
    /// 使用场景：查看系统中所有已定义的作业列表。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业摘要列表。</returns>
    Task<List<JobSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取作业详情。
    /// 使用场景：根据作业 ID 获取详细的作业信息和配置。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情，如果不存在则为 null。</returns>
    Task<JobDetail?> GetAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 创建新作业。
    /// 使用场景：定义新的定时或周期性 AI 任务。
    /// </summary>
    /// <param name="request">作业创建请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建的作业详情。</returns>
    Task<JobDetail> CreateAsync(JobCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 更新作业。
    /// 使用场景：修改现有作业的配置，如调度时间、参数等。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="request">作业更新请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的作业详情。</returns>
    Task<JobDetail> UpdateAsync(string jobId, JobUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// 删除作业。
    /// 使用场景：移除不再需要的作业定义。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
    Task<bool> DeleteAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 暂停作业。
    /// 使用场景：临时停止作业执行，稍后可以恢复。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>暂停后的作业详情。</returns>
    Task<JobDetail> PauseAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 恢复作业。
    /// 使用场景：重新启动之前暂停的作业。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>恢复后的作业详情。</returns>
    Task<JobDetail> ResumeAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 立即执行作业。
    /// 使用场景：手动触发作业执行，绕过调度时间。
    /// </summary>
    /// <param name="jobId">作业 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<JobRunResult> RunNowAsync(string jobId, CancellationToken ct = default);
}

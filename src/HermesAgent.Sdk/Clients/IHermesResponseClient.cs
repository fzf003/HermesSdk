namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 响应客户端接口，用于管理 AI 生成的响应。
/// 使用场景：需要创建、继续、检索或删除 AI 响应时使用，如构建响应管理系统或缓存响应。
/// 支持响应的生命周期管理，包括创建新响应、继续现有响应、获取和删除。
/// </summary>
public interface IHermesResponseClient : IDisposable
{
    /// <summary>
    /// 创建新响应。
    /// 使用场景：基于输入文本生成新的 AI 响应。
    /// </summary>
    /// <param name="input">输入文本。</param>
    /// <param name="options">响应选项，如模型、温度等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果。</returns>
    Task<ResponseResult> CreateAsync(string input, ResponseOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 继续现有响应。
    /// 使用场景：基于之前的响应 ID 和新输入，继续生成响应，如多轮对话或扩展内容。
    /// </summary>
    /// <param name="previousResponseId">之前的响应 ID。</param>
    /// <param name="input">新输入文本。</param>
    /// <param name="options">响应选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果。</returns>
    Task<ResponseResult> ContinueAsync(string previousResponseId, string input, ResponseOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 获取响应。
    /// 使用场景：根据响应 ID 检索已生成的响应内容。
    /// </summary>
    /// <param name="responseId">响应 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果，如果不存在则为 null。</returns>
    Task<ResponseResult?> GetAsync(string responseId, CancellationToken ct = default);

    /// <summary>
    /// 删除响应。
    /// 使用场景：清理不再需要的响应数据。
    /// </summary>
    /// <param name="responseId">响应 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
    Task<bool> DeleteAsync(string responseId, CancellationToken ct = default);
}

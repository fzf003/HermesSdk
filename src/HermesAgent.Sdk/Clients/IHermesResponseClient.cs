using System.Runtime.CompilerServices;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 响应客户端接口，用于管理 AI 生成的响应。
/// 使用场景：需要创建、继续、检索或删除 AI 响应时使用，如构建响应管理系统或缓存响应。
/// 支持响应的生命周期管理，包括创建新响应、获取和删除。
/// 会话状态通过 <c>ResponseOptions.Conversation</c> 传递，客户端无需维护会话 ID。
/// </summary>
public interface IHermesResponseClient : IDisposable
{
    /// <summary>
    /// 创建新响应。
    /// 使用场景：基于输入文本生成新的 AI 响应。
    /// 会话通过 <c>options.Conversation</c> 传递，同一 conversation key 自动归属同一会话。
    /// </summary>
    /// <param name="input">输入文本。</param>
    /// <param name="options">响应选项，如模型、温度、conversation key 等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果。</returns>
    Task<ResponseResult> CreateAsync(dynamic input, ResponseOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 创建流式响应。
    /// 使用场景：需要实时获取 AI 响应的流式输出。
    /// 会话通过 <c>options.Conversation</c> 传递。
    /// </summary>
    /// <param name="input">输入文本。</param>
    /// <param name="options">响应选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>SSE <c>data:</c> 行内容（不含 "data: " 前缀）的异步枚举。</returns>
    IAsyncEnumerable<string> CreateStreamingAsync(dynamic input, ResponseOptions? options = null, CancellationToken ct = default);

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

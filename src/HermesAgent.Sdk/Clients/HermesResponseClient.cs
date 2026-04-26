using System.Net.Http.Json;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 响应客户端实现，用于管理 AI 生成的响应。
/// 使用场景：应用程序需要生成、继续或管理 AI 响应，如内容生成系统或响应缓存服务。
/// 支持响应的创建、继续、检索和删除操作。
/// </summary>
public class HermesResponseClient : IHermesResponseClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 HermesResponseClient 实例。
    /// </summary>
    /// <param name="httpClient">用于发送 HTTP 请求的 HttpClient 实例。</param>
    public HermesResponseClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 创建新响应实现。
    /// 使用场景：基于输入文本生成新的 AI 响应。
    /// </summary>
    /// <param name="input">输入文本。</param>
    /// <param name="options">响应选项，如模型、温度等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果。</returns>
    public async Task<ResponseResult> CreateAsync(string input, ResponseOptions? options = null, CancellationToken ct = default)
    {
        var request = new ResponseRequest
        {
            Model = options?.Model ?? "default",
            Input = input,
            Instructions = options?.Instructions,
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            Metadata = options?.Metadata
        };

        var response = await _httpClient.PostAsJsonAsync("/v1/responses", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ResponseResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid response result");
    }

    /// <summary>
    /// 继续现有响应实现。
    /// 使用场景：基于之前的响应 ID 和新输入，继续生成响应，如多轮对话或扩展内容。
    /// </summary>
    /// <param name="previousResponseId">之前的响应 ID。</param>
    /// <param name="input">新输入文本。</param>
    /// <param name="options">响应选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果。</returns>
    public async Task<ResponseResult> ContinueAsync(string previousResponseId, string input, ResponseOptions? options = null, CancellationToken ct = default)
    {
        var request = new ResponseRequest
        {
            Model = options?.Model ?? "default",
            Input = input,
            Instructions = options?.Instructions,
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            PreviousResponseId = previousResponseId,
            Metadata = options?.Metadata
        };

        var response = await _httpClient.PostAsJsonAsync("/v1/responses", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ResponseResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid response result");
    }

    /// <summary>
    /// 获取响应实现。
    /// 使用场景：根据响应 ID 检索已生成的响应内容。
    /// </summary>
    /// <param name="responseId">响应 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应结果，如果不存在则为 null。</returns>
    public async Task<ResponseResult?> GetAsync(string responseId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/v1/responses/{responseId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<ResponseResult>(cancellationToken: ct);
    }

    /// <summary>
    /// 删除响应实现。
    /// 使用场景：清理不再需要的响应数据。
    /// </summary>
    /// <param name="responseId">响应 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
    public async Task<bool> DeleteAsync(string responseId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/v1/responses/{responseId}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 释放资源。目前无资源需要释放。
    /// </summary>
    public void Dispose() { }
}

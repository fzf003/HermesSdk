using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace HermesAgent.Sdk;

/// <summary>
/// Hermes 响应客户端实现，用于管理 AI 生成的响应。
/// 使用场景：应用程序需要生成、管理 AI 响应，如内容生成系统或响应缓存服务。
/// 支持响应的创建、检索和删除操作。
/// 会话状态通过 <c>ResponseOptions.Conversation</c> 传递，不维护客户端会话 ID。
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
    /// </summary>
    public async Task<ResponseResult> CreateAsync(string input, ResponseOptions? options = null, CancellationToken ct = default)
    {
        var request = new ResponseRequest
        {
            Model = options?.Model ?? "default",
            Input = input,
            Instructions = options?.Instructions,
            Conversation = options?.Conversation,
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            Metadata = options?.Metadata
        };

        using var reqmessage = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(request)
        };

        var response = await _httpClient.SendAsync(reqmessage, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ResponseResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Invalid response result");
    }

    /// <summary>
    /// 创建流式响应，返回 SSE 数据行流。
    /// </summary>
    public async IAsyncEnumerable<string> CreateStreamingAsync(string input, ResponseOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ResponseRequest
        {
            Model = options?.Model ?? "default",
            Input = input,
            Instructions = options?.Instructions,
            Conversation = options?.Conversation,
            Stream = true,
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            Metadata = options?.Metadata
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/responses?stream=true")
        {
            Content = JsonContent.Create(request),
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        httpResponse.EnsureSuccessStatusCode();
        using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? lastEventType = null;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
            {
                lastEventType = null;
                continue;
            }
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                lastEventType = line[7..];
                continue;
            }
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line[6..];
                if (lastEventType is not null && !data.Contains("\"type\""))
                {
                    data = "{\"type\":\"" + lastEventType + "\"," + data.TrimStart('{');
                }
                yield return data;
                continue;
            }
        }
    }

    /// <summary>
    /// 获取响应实现。
    /// </summary>
    public async Task<ResponseResult?> GetAsync(string responseId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/v1/responses/{responseId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<ResponseResult>(cancellationToken: ct);
    }

    /// <summary>
    /// 删除响应实现。
    /// </summary>
    public async Task<bool> DeleteAsync(string responseId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/v1/responses/{responseId}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose() { }
}

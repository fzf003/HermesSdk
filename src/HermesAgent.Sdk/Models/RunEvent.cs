using System.Text.Json.Serialization;

namespace HermesAgent.Sdk;

public record RunEvent
{
    [JsonPropertyName("event")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; init; }

    [JsonPropertyName("output")]
    public string OutPut { get; init; } = string.Empty;
    /// <summary>
    /// 思考链
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
    //是否人工审批
    public bool IsApproval()
    {
        return this.Type == "approval.request";
    }
}

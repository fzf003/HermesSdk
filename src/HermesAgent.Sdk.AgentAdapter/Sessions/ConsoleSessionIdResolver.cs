namespace HermesAgent.Sdk.AgentAdapter.Sessions;

/// <summary>
/// 控制台专用的 SessionId 解析器，从本地文件读取/保存 SessionId。
/// 适用于控制台应用、CLI 工具等非 Web 场景。
/// </summary>
public class ConsoleSessionIdResolver : ISessionIdResolver
{
    private readonly string _sessionFile;

    /// <summary>
    /// 初始化控制台 SessionId 解析器。
    /// </summary>
    /// <param name="sessionFile">会话文件路径，默认 ".hermes-session"。</param>
    public ConsoleSessionIdResolver(string sessionFile = ".hermes-session")
    {
        _sessionFile = sessionFile;
    }

    /// <inheritdoc />
    public string? Resolve(object? context = null)
    {
        if (File.Exists(_sessionFile))
        {
            var sessionId = File.ReadAllText(_sessionFile).Trim();
            if (!string.IsNullOrEmpty(sessionId))
            {
                return sessionId;
            }
        }

        return null;
    }

    /// <summary>
    /// 保存 SessionId 到本地文件。
    /// </summary>
    /// <param name="sessionId">要保存的 SessionId。</param>
    public void Save(string sessionId)
    {
        File.WriteAllText(_sessionFile, sessionId);
    }

    /// <summary>
    /// 删除会话文件。
    /// </summary>
    public void Clear()
    {
        if (File.Exists(_sessionFile))
        {
            File.Delete(_sessionFile);
        }
    }
}

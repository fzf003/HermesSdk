using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// Extension methods for <see cref="IChatClient"/> to create <see cref="ChatClientAgent"/> instances
/// backed by the Hermes Agent SDK adapter.
///
/// <para>This follows the same pattern as Anthropic's <c>AsAIAgent</c> / Ollama's <c>AsAIAgent</c>
/// in the official MAF ecosystem — one method to go from <c>IChatClient</c> to <c>ChatClientAgent</c>.</para>
/// </summary>
public static class HermesChatClientExtensions
{
    /// <summary>
    /// Creates a <see cref="ChatClientAgent"/> from the Hermes MAF <c>IChatClient</c> adapter.
    /// </summary>
    /// <param name="chatClient">The Hermes MAF <c>IChatClient</c> adapter instance.</param>
    /// <param name="model">The model to use (e.g., "default").</param>
    /// <param name="instructions">Optional system instructions for the agent.</param>
    /// <param name="name">Optional agent name.</param>
    /// <param name="description">Optional agent description.</param>
    /// <param name="tools">Optional tools available to the agent.</param>
    /// <param name="clientFactory">Optional delegate to customize the <c>IChatClient</c> pipeline
    /// (e.g., to inject middleware like <c>UseFunctionInvocation</c>).</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A <see cref="ChatClientAgent"/> backed by the Hermes MAF adapter.</returns>
    public static ChatClientAgent AsHermesAIAgent(
        this IChatClient chatClient,
        string model,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        var options = new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
        };

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            options.ChatOptions ??= new();
            options.ChatOptions.Instructions = instructions;
        }

        if (tools is { Count: > 0 })
        {
            options.ChatOptions ??= new();
            options.ChatOptions.Tools = tools;
        }

        var finalClient = chatClient;

        if (clientFactory is not null)
        {
            finalClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(finalClient, options, loggerFactory, services: null);
    }
    /// <summary>
    /// 创建 Hermes 会话（简化版）。
    /// 生成固定 conversation key 并注入 HermesContext，返回普通 AgentSession。
    /// 所有轮次使用同一个 key，无需更新。
    /// </summary>
    public static async Task<AgentSession> CreateHermesSessionAsync(this ChatClientAgent agent, CancellationToken cancellationToken = default)
    {
        var conversationKey = Guid.NewGuid().ToString("N");
        HermesContext.SetConversationId(conversationKey);
        return await agent.CreateSessionAsync(cancellationToken);
    }
}

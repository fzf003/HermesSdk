using HermesAgent.Sdk.AgentAdapter;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;

/// <summary>
/// Extension methods for creating <see cref="HermesAgent"/> instances.
/// </summary>
public static class HermesAgentExtensions
{
    /* /// <summary>
   /// Creates a <see cref="HermesAgent"/> from a <see cref="IHermesResponseClient"/>.
   /// The agent manages its own session via <see cref="HermesAgentSession"/>
   /// with an auto-generated topic ID as the conversation key.
   /// </summary>
   /// <param name="responseClient">The Hermes Responses API client.</param>
   /// <param name="name">Optional agent name (default "HermesAgent").</param>
   /// <param name="description">Optional agent description.</param>
   /// <param name="instructions">Optional system instructions for the agent.</param>
   /// <param name="loggerFactory">Logger factory.</param>
   /// <returns>A configured <see cref="HermesAgent"/> instance.</returns>
   public static HermesAgent CreateHermesAgent(
       this IHermesResponseClient responseClient,
       string name = "HermesAgent",
       string? description = null,
       string? instructions = null,
       ILoggerFactory? loggerFactory = null)
   {
       ArgumentNullException.ThrowIfNull(responseClient);

       var logger = loggerFactory?.CreateLogger<HermesAgent>()
           ?? throw new InvalidOperationException("ILoggerFactory is required to create HermesAgent");

       return new HermesAgent(
           name: name,
           description: description,
           responseClient: responseClient,
           logger: logger,
           instructions: instructions);
   }*/

    /// <summary>
    /// 设置 Session 状态信息
    /// </summary>
    public static AgentSession SetHermesSessionInfo<T>(this AgentSession session, string sessionKey, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        session?.StateBag?.SetValue(sessionKey, value);
        return session;
    }

    /// <summary>
    /// 获取 Session 状态信息，不存在时返回 null
    /// </summary>
    public static T? GetHermesSessionInfo<T>(this AgentSession session, string sessionKey) where T : class
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        return session?.StateBag?.GetValue<T>(sessionKey);
    }

      

    public static ChatClientAgent AsHermesAIAgent(
        this IChatClient chatClient,
        string model="default",
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

}

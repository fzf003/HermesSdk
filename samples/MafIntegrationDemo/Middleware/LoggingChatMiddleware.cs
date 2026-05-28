using System.Diagnostics;
using System.Threading.Tasks;
using HermesAgent.Sdk;
using HermesAgent.Sdk.MicrosoftAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;
namespace MafIntegrationDemo.Middleware
{





    /// <summary>
    ///  client pipeline middleware
    /// </summary> <summary>
    /// 
    /// </summary>
    public sealed class LoggingChatMiddleware : DelegatingChatClient
    {
        private readonly ILogger _logger;

        public LoggingChatMiddleware(IChatClient innerClient, ILogger logger)
            : base(innerClient)
        {
            _logger = logger;
        }
        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {

            _logger.LogInformation("Chat 客户端请求中间件被调用开始。消息数量: {Count}", messages.Count());
            _logger.LogInformation(
                "[Chat ↓] Model={Model}, Messages={Count}",
                options?.ModelId,
                messages.Count()
            );


            var stopwatch = Stopwatch.StartNew();
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            stopwatch.Stop();

            if (options?.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var convId) == true)
            {
                response.ConversationId = convId?.ToString();
            }

            _logger.LogInformation(
                "[Chat ↑] Latency={Ms}ms, Tokens={In}→{Out}",
                stopwatch.ElapsedMilliseconds,
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount
            );

            _logger.LogInformation("Chat 客户端请求中间件被调用结束。消息数量: {Count}", messages.Count());

            _logger.LogInformation($"此次消耗Token:Input:{response.Usage?.InputTokenCount},Output:{response.Usage?.OutputTokenCount} Cache:{response.Usage?.CachedInputTokenCount} 合计:{response.Usage?.TotalTokenCount}");

            return response;
        }
    }


    /// <summary>
    /// agent middleware
    /// </summary>

    public sealed class LoggingAgentMiddleware : DelegatingAIAgent
    {
        private readonly ILogger _logger;

        public LoggingAgentMiddleware(AIAgent innerAgent, ILogger logger)
            : base(innerAgent)
        {
            _logger = logger;
        }

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var sessionId = session is ChatClientAgentSession ccas
                ? ccas.ConversationId ?? "(no conv id)"
                : session?.GetHashCode().ToString() ?? "(null)";

            _logger.LogInformation("Agent 请求中间件被调用开始。消息数量: {Count}", messages.Count());

            _logger.LogInformation("[Agent ▶] Session={SessionId}, Messages={Count}",
                sessionId, messages.Count());

            var stopwatch = Stopwatch.StartNew();
            var response = await base.RunCoreAsync(messages, session, options, cancellationToken);
            stopwatch.Stop();

            var tools = response.Messages
                .SelectMany(m => m.Contents ?? [])
                .OfType<FunctionCallContent>();


            _logger.LogInformation("[Agent ◀] Latency={Ms}ms, ToolCalls={Tools}",
                stopwatch.ElapsedMilliseconds, tools.Count());

            foreach (var msg in tools)
            {
                _logger.LogInformation(
                    "  - Tool call: {ToolName}({Arguments})",
                    msg.Name,
                    msg.Arguments);
            }

            _logger.LogInformation("Agent 请求中间件被调用结束。消息数量: {Count}", messages.Count());

            return response;
        }
    }

}
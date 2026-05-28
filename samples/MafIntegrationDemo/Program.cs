using HermesAgent.Sdk.Extensions;
using HermesAgent.Sdk.MicrosoftAgent;
using MafIntegrationDemo.Middleware;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Text.Json;

using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHermesAgent(context.Configuration)
                .AddHermesAgentMaf(context.Configuration, maf =>
               {
                   maf.EnableAutoSession = true;
                   maf.EnableOpenTelemetry = false;
               });
    })
    .Build();

bool IsStream = true;
var chatClient = host.Services.GetRequiredService<IChatClient>();

var loggerfactory = LoggerFactory.Create(static builder =>
                       builder.AddConsole(
                           opt => opt.LogToStandardErrorThreshold = LogLevel.Information
                       ));

Console.WriteLine("Hermes SDK + Microsoft Agent Framework 集成示例");
Console.WriteLine("================================================");
Console.WriteLine();

var chatclient = chatClient
     .AsBuilder().Use(client => new LoggingChatMiddleware(client, loggerfactory.CreateLogger<LoggingChatMiddleware>()))
     .UseFunctionInvocation()
     .Build();

CancellationTokenSource cancellationToken = new CancellationTokenSource();

// ──────────────────────────────────────────────
//  1. Chat Completions path (no tools)
//     Uses Responses API via IHermesResponseClient
// ──────────────────────────────────────────────

Console.WriteLine("[1] 普通对话（Responses API）");
Console.WriteLine("    Conversation ID 自动生成（AutoSessionMiddleware）\n");

var agent = chatclient.AsHermesAIAgent(model: "default", instructions: "你是一个股票分析师", name: "JockAgent", tools: [DateTimeTool.Create()]);

// 生成固定的 topic key，多轮对话共享同一话题
var topicKey = "topic-demo-9d1926b4"; //"topic-demo-" + Guid.NewGuid().ToString("N")[..8];

var session = await agent.CreateSessionAsync(cancellationToken.Token);

Console.WriteLine($"话题 ID: {topicKey}");
Console.WriteLine("对话开始（输入 exit 退出）:");
Console.Write("你: ");
var inputprompt = Console.ReadLine();

while (!cancellationToken.Token.IsCancellationRequested && inputprompt != "exit")
{
    var options = new AgentRunOptions
    {
        ResponseFormat = ChatResponseFormat.Text,
        AllowBackgroundResponses = false,
    };

    // 注入话题 ID，确保多轮对话上下文延续
    options.AdditionalProperties = new AdditionalPropertiesDictionary
    {
        ["hermes-conversation-id"] = topicKey
    };

    List<ChatMessage> messages = [new ChatMessage(ChatRole.User, inputprompt)];

    Console.Write("AI: ");

    if (IsStream)
    {
        await foreach (var update in agent.RunStreamingAsync(messages: messages, session: session, options: options))
        {
            Console.Write(update.Text);
        }
    }
    else
    {

        var response = await agent.RunAsync(messages: messages, session: session, options: options);

        Console.Write(response.Text);
    }

    Console.WriteLine();

    Console.Write("你: ");
    inputprompt = Console.ReadLine();
}

Console.WriteLine("\n运行结束。");

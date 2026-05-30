using HermesAgent.Sdk;
using HermesAgent.Sdk.AgentAdapter;
using HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;
using HermesAgent.Sdk.AgentAdapter.Sessions;
using HermesAgent.Sdk.Extensions;
using MafIntegrationDemo.Middleware;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Text.Json;

using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHermesAgent(context.Configuration)
                .AddHermesAgentAdapter(context.Configuration)
                .AddAgentSessionStoreForConsole(options =>
                {
                    options.SessionFile = ".hermes-session";
                    options.DefaultExpiration = TimeSpan.FromHours(2);
                });
    })
    .Build();

var loggerfactory = LoggerFactory.Create(static builder => builder.AddConsole(opt => opt.LogToStandardErrorThreshold = LogLevel.Debug));
bool IsStream = false;

// 获取会话存储和解析器
var sessionStore = host.Services.GetRequiredService<IAgentSessionStore>();
var sessionIdResolver = host.Services.GetRequiredService<ISessionIdResolver>();

// 获取 IChatClient 并创建 Agent
var chatClient = host.Services.GetRequiredService<IChatClient>();
var hermesAgent = chatClient.AsHermesAIAgent(
    model: "default",
    instructions: "你是一个股票分析师",
    name: "JockAgent",
    loggerFactory: loggerfactory,
    tools: []);
var agent = hermesAgent.AsBuilder().UseLogging(loggerfactory).Build();

 

Console.WriteLine("Hermes SDK + Microsoft Agent Framework 集成示例");
Console.WriteLine("================================================");
Console.WriteLine();

CancellationTokenSource cancellationToken = new CancellationTokenSource();

// ──────────────────────────────────────────────
//  1. HermesAgent 对话
//     会话通过 AgentSession.StateBag 管理
// ──────────────────────────────────────────────

Console.WriteLine("[1] HermesAgent 对话");
Console.WriteLine("    会话通过 AgentSession 管理\n");

// 从本地文件读取 SessionId
var savedSessionId = sessionIdResolver?.Resolve();

// 如果没有保存的 SessionId，提示用户输入或自动生成
string? userSessionId = null;
if (string.IsNullOrEmpty(savedSessionId))
{
    Console.WriteLine("未找到已保存的会话，请输入 SessionId（留空自动生成）:");
    Console.Write("SessionId: ");
    userSessionId = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(userSessionId))
    {
        userSessionId = Guid.NewGuid().ToString("N");
        Console.WriteLine($"已生成 SessionId: {userSessionId}");
    }
}
else
{
    Console.WriteLine($"已恢复会话: {savedSessionId}");
    userSessionId = savedSessionId;
}

// 立即保存 SessionId 到文件，防止中断丢失
if (!string.IsNullOrEmpty(userSessionId) && sessionIdResolver is ConsoleSessionIdResolver cr)
    cr.Save(userSessionId);

// 加载或创建 AgentSession
AgentSession? session = null;
if (!string.IsNullOrEmpty(userSessionId))
{
    var savedJson = await sessionStore.LoadAsync(userSessionId);
    if (savedJson != null)
    {
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(savedJson);
        session = await agent.DeserializeSessionAsync(jsonElement);
        Console.WriteLine($"已恢复 AgentSession");
    }

}

if (session == null)
{
    session = await agent.CreateSessionAsync(cancellationToken.Token);
    Console.WriteLine("已创建新 AgentSession");
}

// 按设计文档：SessionId 存入 AgentSession.StateBag
session.SetHermesSessionInfo("hermes_conversationId", userSessionId);
session.SetHermesSessionInfo("userinfo", new UserInfo(id: "fzf003", age: 12));


var options = new AgentRunOptions
{
    ResponseFormat = ChatResponseFormat.Text,
    AllowBackgroundResponses = false,
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        ["hermes-conversation-id"] = session.GetHermesSessionInfo<string>("hermes_conversationId"),
    },
};

Console.WriteLine($"会话 ID: {userSessionId}");
Console.WriteLine("对话开始（输入 exit 退出）:");
Console.Write("你: ");
var inputprompt = Console.ReadLine();

while (!cancellationToken.Token.IsCancellationRequested && inputprompt != "exit")
{
    var userinfo = session.GetHermesSessionInfo<UserInfo>("userinfo");
    Console.WriteLine(userinfo);

    List<ChatMessage> messages = [new ChatMessage(ChatRole.User, inputprompt)];

    Console.Write("AI: ");

    if (IsStream)
    {
        await foreach (var update in agent.RunStreamingAsync(messages: messages, session: session,options:options))
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
    Console.WriteLine();

    // 每轮对话后立即保存，防止崩溃丢失
    if (!string.IsNullOrEmpty(userSessionId))
    {
        var jsonElement = await agent.SerializeSessionAsync(session);
        var json = jsonElement.GetRawText();
        await sessionStore.SaveAsync(userSessionId, json);

        PrintResponse(session);
    }

    Console.Write("你: ");
    inputprompt = Console.ReadLine();
}


static void PrintResponse(dynamic response)
{
    Console.WriteLine("*********************************Start***********************************************");
    JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    Console.Write(JsonSerializer.Serialize(response, _jsonOptions));
    Console.WriteLine("***********************************END*********************************************");
}


Console.WriteLine("\n运行结束。");


record UserInfo(string id,int age)
{

}
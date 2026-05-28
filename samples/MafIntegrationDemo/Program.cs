using Google.Protobuf;
using HermesAgent.Sdk.Extensions;
using HermesAgent.Sdk.MicrosoftAgent;
using MafIntegrationDemo.Middleware;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
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
                   maf.EnableRunMiddleware = true;//启用Run
                   maf.EnableOpenTelemetry = false;
               });
    })
    .Build();

bool IsStreamable = false;//是否启用

var chatClient = host.Services.GetRequiredService<IChatClient>();

var loggerfactory = LoggerFactory.Create(static builder =>
                       builder.AddConsole(
                           opt => opt.LogToStandardErrorThreshold = LogLevel.Information
                       ));

Console.WriteLine("Hermes SDK + Microsoft Agent Framework 集成示例");
Console.WriteLine("================================================");
Console.WriteLine();


var chatclient = chatClient
     .AsBuilder().Use(client => new LoggingChatMiddleware(client, loggerfactory.CreateLogger<LoggingChatMiddleware>()))//chatclient 中间件
     .UseFunctionInvocation()
     .Build();

CancellationTokenSource cancellationToken = new CancellationTokenSource();
// ──────────────────────────────────────────────
//  1. Chat Completions path (no tools)
//     Tools = null -> /v1/chat/completions
// ──────────────────────────────────────────────

Console.WriteLine("[1] 普通对话（Chat Completions）");
Console.WriteLine("    Tools = null -> IChatClient -> 适配器 -> /v1/chat/completions\n");

var agent = chatclient.AsHermesAIAgent(model: "default", instructions: "你是一个股票分析师", name: "JockAgent", tools: [DateTimeTool.Create()]);
//.AsBuilder().Use(agent => new LoggingAgentMiddleware(agent, loggerfactory.CreateLogger<LoggingAgentMiddleware>())).Build();

// 生成固定 conversation key，多轮对话共享同一会话
var conversationKey = "fzf-00123"; //"fzf-00121";
AgentSession? session = await agent.CreateSessionAsync(cancellationToken.Token);

Console.WriteLine("对话开始（输入 exit 退出）:");
Console.Write("你: ");
var inputprompt = Console.ReadLine();

HermesContext.SetConversationId(conversationKey);

while (!cancellationToken.Token.IsCancellationRequested && inputprompt != "exit")
{
     List<ChatMessage> messages = [new ChatMessage(ChatRole.User, inputprompt)];

    Console.Write("AI: ");

    if(IsStreamable)
    {
        await foreach (var update in agent.RunStreamingAsync(messages: messages, session: session, options: new AgentRunOptions
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.Text,
            AllowBackgroundResponses = false,
        }))
        {
            if (!string.IsNullOrEmpty(update.Text))
                Console.Write(update.Text);
        }
    }
    else
    {
        var response = await agent.RunAsync(messages: messages, session: session, options: new AgentRunOptions
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.Text,
            AllowBackgroundResponses = false,
        });

        Console.Write(response.Text);
    }
    
    
   
    Console.WriteLine();

   PrintResponse(session);

    Console.Write("你: ");
    inputprompt = Console.ReadLine();
}
/*
var response1 = await chatClient.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "今天北京的天气如何？")]);

Console.WriteLine($"    回复: {response1.Messages[0].Text}\n");
*/

// ──────────────────────────────────────────────
//  2. Responses API path (with tools)
//     Tools != null -> /v1/responses
// ──────────────────────────────────────────────

Console.WriteLine("\r\n [2] 带工具调用（Responses API）");
Console.WriteLine("    Tools 作为路由信号 -> /v1/responses\n");




var timeagent = chatclient.AsAIAgent(instructions: "你是一个时间助手", name: "TimeAgent", loggerFactory: loggerfactory).AsBuilder().Use(agent => new LoggingAgentMiddleware(agent, loggerfactory.CreateLogger<LoggingAgentMiddleware>())).Build();

var timesession = await timeagent.CreateSessionAsync();

List<ChatMessage> timemessages = [new ChatMessage(ChatRole.User, "今天洛阳天气怎么样?")];

var timeresponse = await timeagent.RunAsync(messages: timemessages, session: timesession);

Console.WriteLine(timeresponse.Text);
/*
await foreach(var update in timeagent.RunStreamingAsync(messages: timemessages, session: timesession))
{
    PrintResponse(update);
}*/
PrintResponse(timesession);

Console.ReadKey();






// ──────────────────────────────────────────────
//  3. Streaming
//     GetStreamingResponseAsync -> SSE streaming
// ──────────────────────────────────────────────
/*
Console.WriteLine("[3] 流式对话\n");

Console.Write("    AI: ");
await foreach (var update in chatClient.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "从 1 数到 5")]))
{
    if (!string.IsNullOrEmpty(update.Text))
        Console.Write(update.Text);
}
Console.WriteLine("\n");

// ──────────────────────────────────────────────
//  4. Run middleware (long task -> Hermes Run + SSE)
//     UseHermesRun() flag triggers middleware routing
// ──────────────────────────────────────────────

Console.WriteLine("[4] Run 中间件模式");
Console.WriteLine("    UseHermesRun() 标记 -> Run + SSE\n");

try
{
    var runOptions = new ChatOptions().UseHermesRun();
    runOptions.ModelId = "default";

    var response4 = await chatClient.GetResponseAsync(
        [new ChatMessage(ChatRole.User, "What is 2+2? Explain step by step.")],
        runOptions);

    Console.WriteLine($"    回复: {response4.Messages[0].Text}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"    Run 错误: {ex.Message}");
}
*/

Console.WriteLine("\n运行结束。");

// ──────────────────────────────────────────────
//  Helper: print full tool-call chain
// ──────────────────────────────────────────────

static void PrintToolChain(IList<ChatMessage> messages)
{
    foreach (var msg in messages)
    {
        if (msg.Contents is not { Count: > 0 }) continue;

        foreach (var content in msg.Contents)
        {
            switch (content)
            {
                case FunctionCallContent fc:
                    Console.WriteLine($"      🔧 {fc.Name}({JsonSerializer.Serialize(fc.Arguments)})");
                    break;
                case FunctionResultContent fr:
                    var preview = fr.Result?.ToString() ?? "";
                    if (preview.Length > 200) preview = preview[..200] + "...";
                    Console.WriteLine($"      📊 → {preview}");
                    break;
                case TextContent t:
                    Console.WriteLine($"      💬 {t.Text}");
                    break;
            }
        }
    }
}


static void PrintResponse(dynamic response)
{
    Console.WriteLine("*********************************Start***********************************************");
    JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    Console.Write(JsonSerializer.Serialize(response, _jsonOptions));
    Console.WriteLine("***********************************END*********************************************");
}


// ──────────────────────────────────────────────
//  Helper: extract final output from tool chain
// ──────────────────────────────────────────────

static string ExtractFinalOutput(IList<ChatMessage> messages)
{
    foreach (var msg in messages)
    {
        if (msg.Contents is not { Count: > 0 }) continue;

        // Prefer TextContent (AI's final text answer), otherwise use last FunctionResultContent
        var texts = msg.Contents.OfType<TextContent>().ToList();
        if (texts.Count > 0)
            return string.Join("\n", texts.Select(t => t.Text));

        var lastResult = msg.Contents.OfType<FunctionResultContent>().LastOrDefault();
        if (lastResult?.Result is string resultStr)
        {
            // Try to extract the "output" field from JSON result
            try
            {
                using var doc = JsonDocument.Parse(resultStr);
                if (doc.RootElement.TryGetProperty("output", out var output))
                    return output.GetString() ?? resultStr;
            }
            catch (JsonException) { }
            return resultStr;
        }
    }

    return messages[^1].Text ?? "(empty)";
}

using HermesAgent.Sdk;
using HermesAgent.Sdk.AgentAdapter;
using HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;
using HermesAgent.Sdk.AgentAdapter.Sessions;
using HermesAgent.Sdk.Extensions;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;

var builder = WebApplication.CreateBuilder(args);

// Logging — align with MafIntegrationDemo
builder.Logging.AddConsole(opt => opt.LogToStandardErrorThreshold = LogLevel.Debug);

// Register Hermes core clients
builder.Services.AddHermesAgent(builder.Configuration)
       .AddHermesAgentAdapter(builder.Configuration)
       .AddAgentSessionStoreHybrid(options =>
       {
           options.DefaultExpiration = TimeSpan.FromMinutes(60);
           options.AuthMethod = SessionMethod.Header;
       }).AddSingleton<AIAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return chatClient.AsHermesAIAgent(model: "default", loggerFactory: loggerFactory);
        });



builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

app.UseSwagger();
app.UseSwaggerUI();

// ──────────────────────────────────────────────
//  Chat endpoint — IChatClient path
//  POST /api/chat
// ──────────────────────────────────────────────
app.MapPost("/api/chat", async (
    ChatRequest request,
    IChatClient chatClient,
    HttpContext httpContext) =>
{
    try
    {
        var options = new ChatOptions { ModelId = "default" };

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = request.SessionId
            };
        }

        options.Instructions = "";

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, request.Prompt!)],
            options);

        var conversation = options.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var conv) == true
            ? conv?.ToString()
            : null;

        return Results.Ok(new ChatResponseData(
            Reply: response.Text,
            ConversationId: conversation
        ));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Chat request failed: {ex.Message}");
    }
})
.WithName("Chat")
.WithTags("Chat");

// ──────────────────────────────────────────────
//  Streaming chat endpoint — SSE
//  POST /api/chat/stream
// ──────────────────────────────────────────────
app.MapPost("/api/chat/stream", async (
    ChatRequest request,
    IChatClient chatClient,
    HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers["Cache-Control"] = "no-cache";
    http.Response.Headers["Connection"] = "keep-alive";

    try
    {
        var options = new ChatOptions { ModelId = "default" };

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = request.SessionId
            };
        }

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, request.Prompt!)],
            options))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                await http.Response.WriteAsync($"data: {update.Text}\n\n");
                await http.Response.Body.FlushAsync();
            }
        }

        var conversation = options.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var conv) == true
            ? conv?.ToString()
            : "";
        await http.Response.WriteAsync($"data: [DONE] conversationId={conversation}\n\n");
        await http.Response.Body.FlushAsync();
    }
    catch (Exception ex)
    {
        await http.Response.WriteAsync($"data: [ERROR] {ex.Message}\n\n");
        await http.Response.Body.FlushAsync();
    }
})
.WithName("ChatStream")
.WithTags("Chat");

// ──────────────────────────────────────────────
//  AIAgent endpoint — ChatClientAgent + Session
//  支持多轮对话：传 sessionId 复用话题，不传则新建
//  POST /api/chat/agent
// ──────────────────────────────────────────────
app.MapPost("/api/chat/agent", async (
    AgentChatRequest request,
    IAgentSessionStore sessionStore,
    ISessionIdResolver sessionIdResolver,
    HttpContext httpContext,
    AIAgent agent) =>
{
    try
    {
        // 解析 SessionId
        var sessionId = request.SessionId ?? sessionIdResolver.Resolve(httpContext);

        // 加载或创建 AgentSession
        AgentSession? session = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var savedJson = await sessionStore.LoadAsync(sessionId);
            if (savedJson != null)
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(savedJson);
                session = await agent.DeserializeSessionAsync(jsonElement);
            }
        }

        session ??= await agent.CreateSessionAsync(CancellationToken.None);

        // 生成或使用现有的 SessionId
        sessionId ??= Guid.NewGuid().ToString("N");

        // 按设计文档：SessionId 存入 AgentSession.StateBag
        session.SetHermesSessionInfo("hermes_conversationId", sessionId);

        var options = new AgentRunOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = session.GetHermesSessionInfo<string>("hermes_conversationId"),
            },
        };

        // 运行对话
        var response = await agent.RunAsync(request.Prompt!, session, options, CancellationToken.None);

        // 保存会话
        var jsonElement2 = await agent.SerializeSessionAsync(session);
        var json = jsonElement2.GetRawText();
        await sessionStore.SaveAsync(sessionId, json);

        // 根据 SessionMethod 写入 SessionId
        if (sessionIdResolver is CookieSessionIdResolver)
        {
            httpContext.Response.Cookies.Append("ChatSessionId", sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }
        else if (sessionIdResolver is HeaderSessionIdResolver)
        {
            httpContext.Response.Headers["X-Session-Id"] = sessionId;
        }

        return Results.Ok(new AgentChatResponseData(
            Reply: response.Text,
            SessionId: sessionId,
            ConversationId: null,
            ResponseId: response.ResponseId
        ));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Agent request failed: {ex.Message}");
    }
})
.WithName("ChatAgent")
.WithTags("Chat");

// ──────────────────────────────────────────────
//  AIAgent streaming endpoint — ChatClientAgent + Session
//  POST /api/chat/agent/stream
// ──────────────────────────────────────────────
app.MapPost("/api/chat/agent/stream", async (
    AgentChatRequest request,
    IAgentSessionStore sessionStore,
    ISessionIdResolver sessionIdResolver,
    AIAgent agent,
    HttpContext http,
   [FromHeader(Name = "X-Session-Id")] string xsession
    ) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers["Cache-Control"] = "no-cache";
    http.Response.Headers["Connection"] = "keep-alive";

    try
    {
        // 解析 SessionId
        var sessionId = sessionIdResolver.Resolve(http);

        // 加载或创建 AgentSession
        AgentSession? session = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var savedJson = await sessionStore.LoadAsync(sessionId);
            if (savedJson != null)
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(savedJson);
                session = await agent.DeserializeSessionAsync(jsonElement);
            }
        }

        session ??= await agent.CreateSessionAsync(CancellationToken.None);

        // 生成或使用现有的 SessionId
        sessionId ??= Guid.NewGuid().ToString("N");

        // 按设计文档：SessionId 存入 AgentSession.StateBag
        session.SetHermesSessionInfo("hermes_conversationId", sessionId);

        var streamOptions = new AgentRunOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = session.GetHermesSessionInfo<string>("hermes_conversationId"),
            },
        };

        // 运行流式对话
        await foreach (var update in agent.RunStreamingAsync(request.Prompt!, session, streamOptions, CancellationToken.None))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                await http.Response.WriteAsync($"data: {update.Text}\n\n");
                await http.Response.Body.FlushAsync();
            }
        }

        // 保存会话
        var serializedJson = await agent.SerializeSessionAsync(session);
        var json = serializedJson.GetRawText();
        await sessionStore.SaveAsync(sessionId, json);

        // 根据 SessionMethod 写入 SessionId
        if (sessionIdResolver is CookieSessionIdResolver)
        {
            http.Response.Cookies.Append("ChatSessionId", sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }
        else if (sessionIdResolver is HeaderSessionIdResolver)
        {
            http.Response.Headers["X-Session-Id"] = sessionId;
        }

        await http.Response.WriteAsync($"data: [DONE] sessionId={sessionId}\n\n");
        await http.Response.Body.FlushAsync();
    }
    catch (Exception ex)
    {
        await http.Response.WriteAsync($"data: [ERROR] {ex.Message}\n\n");
        await http.Response.Body.FlushAsync();
    }
})
.WithName("ChatAgentStream")
.WithTags("Chat");

app.Run();

// ──────────────────────────────────────────────
//  Models
// ──────────────────────────────────────────────

public record ChatRequest(string? Prompt, string? SessionId = null);
public record ChatResponseData(string Reply, string? ConversationId);
public record AgentChatRequest(string Prompt, string? SessionId = null);
public record AgentChatResponseData(string Reply, string SessionId, string? ConversationId, string? ResponseId);

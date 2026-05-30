using HermesAgent.Sdk;
using HermesAgent.Sdk.AgentAdapter;
using HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;
using HermesAgent.Sdk.AgentAdapter.Sessions;
using HermesAgent.Sdk.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

var builder = WebApplication.CreateBuilder(args);

// Register Hermes core clients
builder.Services.AddHermesAgent(builder.Configuration)
       .AddHermesAgentAdapter(builder.Configuration)
       // 注册会话存储（混合模式：同时支持 Web Cookie 和 API Header）
       .AddAgentSessionStoreHybrid(options =>
       {
           options.DefaultExpiration = TimeSpan.FromMinutes(60);
       });

// ── Swagger ──
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
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

        // 使用请求中的 SessionId（如果有）
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
//  Body: { "prompt": "...", "sessionId": "xxx" }
// ──────────────────────────────────────────────
app.MapPost("/api/chat/agent", async (
    AgentChatRequest request,
    IChatClient chatClient,
    IAgentSessionStore sessionStore,
    ISessionIdResolver sessionIdResolver,
    HttpContext httpContext) =>
{
    try
    {
        // 解析 SessionId
        var sessionId = request.SessionId ?? sessionIdResolver.Resolve(httpContext);

        // 从 IChatClient 创建 ChatClientAgent
        var agent = chatClient.AsHermesAIAgent(model: "default");

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

        // 运行对话
        var response = await agent.RunAsync(request.Prompt!, session, new AgentRunOptions(), CancellationToken.None);

        // 生成或使用现有的 SessionId
        sessionId ??= Guid.NewGuid().ToString("N");

        // 保存会话
        var jsonElement2 = await agent.SerializeSessionAsync(session);
        var json = jsonElement2.GetRawText();
        await sessionStore.SaveAsync(sessionId, json);

        // Web 模式：写入 Cookie
        if (sessionIdResolver is CookieSessionIdResolver)
        {
            var cookieManager = httpContext.RequestServices.GetService<ISessionCookieManager>();
            cookieManager?.SetCookie(sessionId);
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
//  Body: { "prompt": "...", "sessionId": "xxx" }
// ──────────────────────────────────────────────
app.MapPost("/api/chat/agent/stream", async (
    AgentChatRequest request,
    IChatClient chatClient,
    IAgentSessionStore sessionStore,
    ISessionIdResolver sessionIdResolver,
    HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers["Cache-Control"] = "no-cache";
    http.Response.Headers["Connection"] = "keep-alive";

    try
    {
        // 解析 SessionId
        var sessionId = request.SessionId ?? sessionIdResolver.Resolve(http);

        // 从 IChatClient 创建 ChatClientAgent
        var agent = chatClient.AsHermesAIAgent(model: "default");

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

        // 运行流式对话
        await foreach (var update in agent.RunStreamingAsync(request.Prompt!, session, new AgentRunOptions(), CancellationToken.None))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                await http.Response.WriteAsync($"data: {update.Text}\n\n");
                await http.Response.Body.FlushAsync();
            }
        }

        // 生成或使用现有的 SessionId
        sessionId ??= Guid.NewGuid().ToString("N");

        // 保存会话
        var serializedJson = await agent.SerializeSessionAsync(session);
        var json = serializedJson.GetRawText();
        await sessionStore.SaveAsync(sessionId, json);

        // Web 模式：写入 Cookie
        if (sessionIdResolver is CookieSessionIdResolver)
        {
            var cookieManager = http.RequestServices.GetService<ISessionCookieManager>();
            cookieManager?.SetCookie(sessionId);
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

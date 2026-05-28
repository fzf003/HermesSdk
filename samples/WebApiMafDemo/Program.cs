using HermesAgent.Sdk.Extensions;
using HermesAgent.Sdk.MicrosoftAgent;
using Microsoft.Extensions.AI;
using HermesAgent.Sdk;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;



var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHermesAgent(builder.Configuration)
       .AddHermesAgentMaf(builder.Configuration, maf =>
       {
           maf.EnableAutoSession = true;
       });

// ── Swagger ──
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();


// ──────────────────────────────────────────────
//  Chat endpoint — Topic-based conversation
//  POST /api/chat
//  Body: { "prompt": "...", "conversation": "..." }
//        conversation is optional — if omitted,
//        AutoSessionMiddleware generates a Topic ID
// ──────────────────────────────────────────────
app.MapPost("/api/chat", async (ChatRequest request, IChatClient chatClient) =>
{
    try
    {
        var options = new ChatOptions { ModelId = "default" };

        // Inject conversation key into AdditionalProperties
        if (!string.IsNullOrEmpty(request.Conversation))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = request.Conversation
            };
        }

        options.Instructions = "";

        var response = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, request.Prompt)],options);
        
        var conversation = options.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var conv) == true
            ? conv?.ToString()
            : null;

        return Results.Ok(new ChatResponseData(
            Reply: response.Text,
            Conversation: conversation
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
//  Body: { "prompt": "...", "conversation": "..." }
// ──────────────────────────────────────────────
app.MapPost("/api/chat/stream", async (ChatRequest request, IChatClient chatClient, HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers["Cache-Control"] = "no-cache";
    http.Response.Headers["Connection"] = "keep-alive";

    try
    {
        var options = new ChatOptions { ModelId = "default" };

        if (!string.IsNullOrEmpty(request.Conversation))
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["hermes-conversation-id"] = request.Conversation
            };
        }

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, request.Prompt)],
            options))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                await http.Response.WriteAsync($"{update.Text}\n\n");
                await http.Response.Body.FlushAsync();
            }
        }

        // Return the final conversation key
        var conversation = options.AdditionalProperties?.TryGetValue("hermes-conversation-id", out var conv) == true
            ? conv?.ToString()
            : "";
        await http.Response.WriteAsync($"data: [DONE] conversation={conversation}\n\n");
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

app.Run();

// ──────────────────────────────────────────────
//  Request / Response models
// ──────────────────────────────────────────────

public record ChatRequest(string Prompt, string? Conversation = null);

public record ChatResponseData(string Reply, string? Conversation);

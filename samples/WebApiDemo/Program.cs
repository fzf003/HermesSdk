using HermesAgent.Sdk;
using HermesAgent.Sdk.AspNetCore;
using HermesAgent.Sdk.Extensions;
using System.IO;


LoadEnvFile();

var builder = WebApplication.CreateBuilder(args);

// 配置服务
builder.Services.AddHermesAgent(builder.Configuration);

// 启动时校验必要配置，避免静默认证失败
var apiKey = builder.Configuration["HermesAgent:ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("⚠️  警告: HermesAgent:ApiKey 未配置，API 调用将因认证失败而报错。请在 .env 文件中设置 HermesAgent__ApiKey。");
}

// 添加 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

#region webhookcallback

/// <summary>
/// 处理聊天完成事件
/// </summary>
async Task HandleChatCompleted(WebhookCallbackContext context)
{
    // 解析聊天响应数据
    using var jsonDoc = System.Text.Json.JsonDocument.Parse(context.Input);
    var root = jsonDoc.RootElement;

    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
    {
        var message = choices[0].GetProperty("message").GetProperty("content").GetString();
        Console.WriteLine($"💬 聊天完成: {message?.Substring(0, Math.Min(100, message.Length))}...");

        // 这里可以保存到数据库、发送通知�?
        // await SaveChatHistoryAsync(context.DeliveryId, message);
    }
}

/// <summary>
/// 处理运行完成事件
/// </summary>
async Task HandleRunCompleted(WebhookCallbackContext context)
{
    using var jsonDoc = System.Text.Json.JsonDocument.Parse(context.Input);
    var root = jsonDoc.RootElement;

    var runId = root.GetProperty("id").GetString();
    var status = root.GetProperty("status").GetString();

    Console.WriteLine($"🚀 运行完成: {runId}, 状�? {status}");

    // 处理运行结果
    if (root.TryGetProperty("result", out var result))
    {
        // await ProcessRunResultAsync(runId, result);
    }
}

/// <summary>
/// 处理作业完成事件
/// </summary>
async Task HandleJobCompleted(WebhookCallbackContext context)
{
    using var jsonDoc = System.Text.Json.JsonDocument.Parse(context.Input);
    var root = jsonDoc.RootElement;

    var jobId = root.GetProperty("id").GetString();
    var jobType = root.GetProperty("type").GetString();

    Console.WriteLine($"作业完成: {jobId}, 类型: {jobType}");

    // 处理作业结果
    // await UpdateJobStatusAsync(jobId, "completed");
}

/// <summary>
/// 处理失败事件
/// </summary>
async Task HandleFailure(WebhookCallbackContext context)
{
    using var jsonDoc = System.Text.Json.JsonDocument.Parse(context.Input);
    var root = jsonDoc.RootElement;

    var error = root.TryGetProperty("error", out var errorElement)
        ? errorElement.GetProperty("message").GetString()
        : "未知错误";

    Console.WriteLine($"处理失败: {context.EventType}, 错误: {error}");

    // 发送告警通知
    // await SendAlertAsync(context.EventType, error);
}
#endregion

var app = builder.Build();

// 配置中间件
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{

    app.UseHttpsRedirection();
}

// 聊天 API 端点
app.MapPost("/api/chat", async (ChatRequest request, IHermesChatClient chatClient) =>
{
    try
    {
        var response = await chatClient.ChatAsync(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem($"聊天请求失败: {ex.Message}");
    }
})
.WithName("Chat")
.WithTags("Chat");

// 流式聊天 API 端点
app.MapPost("/api/chat/stream", async (ChatRequest request, IHermesChatClient chatClient, HttpResponse response) =>
{
    response.ContentType = "text/plain; charset=utf-8";
    response.Headers["Cache-Control"] = "no-cache";
    response.Headers["Connection"] = "keep-alive";

    try
    {
        request = request with { Stream = true };

        await foreach (var chunk in chatClient.ChatStreamAsync(request))
        {
            if (chunk?.Choices?.Count > 0)
            {
                var deltaContent = chunk.Choices[0].Delta?.Content;
                if (!string.IsNullOrEmpty(deltaContent))
                {
                    await response.WriteAsync(deltaContent);
                    // Consider flushing only when necessary to improve performance
                    // await response.Body.FlushAsync();
                }
            }
        }
    }
    catch (Exception ex)
    {
        response.ContentType = "application/json; charset=utf-8";
        var errorJson = System.Text.Json.JsonSerializer.Serialize(new { error = $"聊天流式请求失败: {ex.Message}" });
        await response.WriteAsync(errorJson);
        await response.WriteAsync($"\n❌ 错误: {ex.Message}");
    }
})
.WithName("ChatStream")
.WithTags("Chat");

// 简单问答 API 端点
app.MapPost("/api/ask", async (AskRequest request, IHermesChatClient chatClient) =>
{
    try
    {
        var response = await chatClient.AskAsync(request.Message, request.SystemPrompt, request.Options);
        return Results.Ok(new { answer = response });
    }
    catch (Exception ex)
    {
        return Results.Problem($"问答请求失败: {ex.Message}");
    }
})
.WithName("Ask")
.WithTags("Chat");





// 配置 Webhook 中间
app.UseHermesWebhook("/webhooks/hermescallback", options =>
{
    options.RequireHttps = app.Environment.IsProduction();
    options.Secret = builder.Configuration["HermesAgent:WebhookSecret"] ?? string.Empty;

    // 配置允许的事件类
    options.AllowedEventTypes = new List<string>
    {
        "chat.completed",
        "run.completed",
        "run.failed",
        "job.completed",
        "job.failed",
        "test"
    };

    // 配置事件处理程序
    options.OnCompletion = async (context) =>
    {
        Console.WriteLine($"📨 收到 Webhook 事件: {context.EventType}");
        Console.WriteLine($"AckId:{context.DeliveryId}");
        Console.WriteLine($"   路由: {context.RouteName}");
        Console.WriteLine($"   输入: {context.Input}");


        // 根据事件类型处理不同的业务逻辑
        switch (context.EventType)
        {
            case "chat.completed" or "test":
                 await HandleChatCompleted(context);
                break;
            case "run.completed" or "test":
                await HandleRunCompleted(context);
                break;
            case "job.completed" or "test":
                await HandleJobCompleted(context);
                break;
            case "run.failed" or "test":
            case "job.failed" or "test":

                await HandleFailure(context);
                break;
            default:
                Console.WriteLine($"⚠️  未处理的事件类型: {context.EventType}");
                break;
        }
    };

    // 配置错误处理程序
    options.OnError = async (exception, context) =>
    {
        Console.WriteLine($"Webhook 处理错误: {exception.Message}");
        Console.WriteLine($"   事件类型: {context.EventType}");
        Console.WriteLine($"   输入: {context.Input}");

        // 这里可以记录到日志系统或发送告�?
        // await LogErrorAsync(exception, context);
    };
});
 


app.Run();

/// <summary>
/// 加载 .env 文件到进程环境变量。仅 Development 环境下生效。
/// </summary>
static void LoadEnvFile()
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    var isDevelopment = string.IsNullOrEmpty(environment) || 
                        environment.Equals("Development", StringComparison.OrdinalIgnoreCase);

    if (!isDevelopment)
        return;

    var envFile = ".env";
    if (!File.Exists(envFile))
        return;

    foreach (var line in File.ReadLines(envFile))
    {
        var trimmed = line.Trim();
        
        // 跳过空行和注释
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            continue;
        
        // 解析 KEY=VALUE 格式
        var parts = trimmed.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            
            // 移除值两边的引号（如果存在）
            if (value.StartsWith('"') && value.EndsWith('"'))
                value = value.Substring(1, value.Length - 2);
            
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

/// <summary>
/// 问答请求模型
/// </summary>
public record AskRequest(string Message, string? SystemPrompt = null, ChatOptions? Options = null);


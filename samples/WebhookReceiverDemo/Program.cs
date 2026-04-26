using HermesAgent.Sdk;
using HermesAgent.Sdk.AspNetCore;
using HermesAgent.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 配置服务
builder.Services.AddHermesAgent(builder.Configuration);

// 添加 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 配置中间
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{

    app.UseHttpsRedirection();
}
 
// 健康检查端
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.Now }));

// Webhook 状态查询端
app.MapGet("/webhooks/status", () =>
{
    return Results.Ok(new
    {
        webhookUrl = $"{builder.Configuration["WebhookBaseUrl"] ?? "https://your-domain.com"}/webhooks/hermes",
        configuredEvents = new[] { "chat.completed", "run.completed", "run.failed", "job.completed", "job.failed" },
        status = "active"
    });
})
.WithName("WebhookStatus")
.WithTags("Webhooks");

// 模拟发Webhook（用于测试）
app.MapPost("/webhooks/dotnet-webhook", async (TestWebhookRequest request, IHermesWebhookClient webhookClient) =>
{
    try
    {
        var result = await webhookClient.SendAsync(request.RouteName, request.EventType, System.Text.Json.JsonSerializer.Serialize(request.Payload), new WebhookOptions
        {
            SignatureSecret = builder.Configuration["HermesAgent:WebhookSecret"]
        });

        return Results.Ok(new { success = result.Status == "accepted", deliveryId = result.DeliveryId });
    }
    catch (Exception ex)
    {
        return Results.Problem($"发Webhook 失败: {ex.Message}");
    }
})
.WithName("TestWebhook")
.WithTags("Webhooks");

app.Run();

 
/// <summary>
/// 测试 Webhook 请求模型
/// </summary>
public record TestWebhookRequest(string RouteName, string EventType, Dictionary<string, object> Payload);

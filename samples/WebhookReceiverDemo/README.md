# WebhookReceiverDemo 示例

这是一个使用 HermesAgent.Sdk.AspNetCore 的 Webhook 接收器示例应用程序。

## 功能特性

- ✅ Webhook 中间件集成
- ✅ 事件类型过滤
- ✅ 安全验证（HTTPS 要求、签名验证）
- ✅ 结构化事件处理
- ✅ 错误处理和日志记录
- ✅ 测试 Webhook 发送功能
- ✅ Swagger/OpenAPI 文档

## 快速开始

### 1. 配置 API Key 和 Webhook Secret

编辑 `appsettings.json` 或使用 User Secrets：

```bash
# 使用 User Secrets（推荐）
dotnet user-secrets set "HermesAgent:ApiKey" "your-actual-api-key"
dotnet user-secrets set "HermesAgent:WebhookSecret" "your-webhook-secret"
dotnet user-secrets set "WebhookBaseUrl" "https://your-domain.com"
```

### 2. 运行应用程序

```bash
dotnet run
```

### 3. 配置 Webhook URL

在 Hermes Agent 管理界面中，将 Webhook URL 配置为：
`https://your-domain.com/webhooks/hermes`

## 支持的事件类型

| 事件类型 | 说明 | 处理逻辑 |
|----------|------|----------|
| `chat.completed` | 聊天完成 | 保存聊天历史 |
| `run.completed` | 运行完成 | 处理运行结果 |
| `run.failed` | 运行失败 | 发送告警通知 |
| `job.completed` | 作业完成 | 更新作业状态 |
| `job.failed` | 作业失败 | 发送告警通知 |

## API 端点

### GET `/health`
健康检查端点。

### GET `/webhooks/status`
查询 Webhook 配置状态。

**响应示例：**
```json
{
  "webhookUrl": "https://your-domain.com/webhooks/hermes",
  "configuredEvents": [
    "chat.completed",
    "run.completed",
    "run.failed",
    "job.completed",
    "job.failed"
  ],
  "status": "active"
}
```

### POST `/test/webhook`
测试 Webhook 发送功能。

**请求示例：**
```json
{
  "targetUrl": "https://httpbin.org/post",
  "eventType": "test.event",
  "payload": {
    "message": "Hello from WebhookReceiverDemo!",
    "timestamp": "2024-01-01T00:00:00Z"
  }
}
```

## 使用场景

- **事件驱动架构**: 接收和处理 Hermes Agent 的事件通知
- **实时数据同步**: 聊天完成时同步数据到外部系统
- **监控和告警**: 运行或作业失败时发送告警
- **业务流程集成**: 将 AI 结果集成到现有业务流程中

## 配置选项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `HermesAgent:BaseUrl` | API 基础地址 | `https://api.hermes-agent.com` |
| `HermesAgent:ApiKey` | API 密钥 | 必需 |
| `HermesAgent:WebhookSecret` | Webhook 签名密钥 | 必需 |
| `HermesAgent:Timeout` | 请求超时时间 | `00:00:30` |
| `WebhookBaseUrl` | Webhook 基础 URL | 必需 |

## 测试 Webhook

### 1. 使用内置测试端点

```bash
curl -X POST "https://localhost:5001/test/webhook" \
     -H "Content-Type: application/json" \
     -d '{
       "targetUrl": "https://httpbin.org/post",
       "eventType": "test.event",
       "payload": {"message": "Hello World"}
     }'
```

### 2. 使用外部工具

可以使用 [ngrok](https://ngrok.com/) 或 [webhook.site](https://webhook.site) 等工具暴露本地端口，然后配置为 Webhook URL。

### 3. 查看日志

应用程序会在控制台输出接收到的 Webhook 事件信息：

```
📨 收到 Webhook 事件: chat.completed
   路由: webhooks/hermes
   输入: {"choices":[{"message":{"content":"Hello!"}}]}
💬 聊天完成: Hello!...
```

## 安全考虑

- **HTTPS**: 生产环境必须启用 HTTPS
- **签名验证**: 配置 Webhook Secret 进行请求签名验证
- **事件过滤**: 只处理配置允许的事件类型
- **错误处理**: 妥善处理异常情况，避免信息泄露

## 扩展开发

### 添加新事件处理

在 `OnCompletion` 委托中添加新的 case：

```csharp
case "your.custom.event":
    await HandleYourCustomEvent(context);
    break;
```

### 自定义中间件选项

```csharp
app.UseHermesWebhook("/webhooks/hermes", options =>
{
    options.RequireHttps = true;
    options.Secret = "your-secret";
    options.AllowedEventTypes = new[] { "your.events" };
    // 自定义处理逻辑
});
```

## 相关链接

- [HermesAgent.Sdk 文档](../../docs/)
- [HermesAgent.Sdk.AspNetCore 文档](../../docs/)
- [Webhook 安全最佳实践](../../docs/webhook-security.md)
- [Swagger 文档](https://localhost:5001/swagger)（运行后访问）
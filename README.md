# HermesAgent.Sdk 

## 项目定位

**HermesAgent.Sdk** 是一套面向 .NET 平台的客户端 SDK，为 C# 应用提供与 Hermes Agent 服务交互的完整能力：
Chat 对话、Responses 响应管理、Runs 异步运行、Jobs 定时作业、Webhook 事件回调。

---

## HermesAgent.Sdk (核心包)

> 版本 `1.0.0-alpha.1` | net10.0 | NuGet: `HermesAgent.Sdk`

**适用于任何 .NET 应用**（Console、ASP.NET Core、Background Service 等），通过依赖注入提供类型安全的客户端接口。

### 模块

| 模块 | 接口 | 能力 |
|------|------|------|
| **Chat** | `IHermesChatClient` | 同步/流式对话、多轮问答 |
| **Responses** | `IHermesResponseClient` | AI 响应创建、继续、检索、删除 |
| **Runs** | `IHermesRunClient` | 异步启动运行 + SSE 事件流订阅 |
| **Jobs** | `IHermesJobClient` | 定时作业的创建/暂停/恢复/执行 |
| **Webhook 发送** | `IHermesWebhookClient` | 向指定路由发送事件 + HMAC 签名 |

### 配置

```csharp
builder.Services.AddHermesAgent(builder.Configuration);
// 自动从 appsettings.json 的 "HermesAgent" 节绑定 HermesAgentOptions
```

---

## HermesAgent.Sdk.AspNetCore (集成扩展包)

> 版本 `1.0.0-beta.1` | net10.0 | NuGet: `HermesAgent.Sdk.AspNetCore`

**仅适用于 ASP.NET Core**，提供 Webhook 接收中间件，依赖 `HermesAgent.Sdk` 核心包。

### 模块

| 模块 | 组件 | 能力 |
|------|------|------|
| **Webhook 接收** | `UseHermesWebhook()` | 注册路由 + 签名验证 + 事件分发 |

### 使用

```csharp
app.UseHermesWebhook("/webhooks/callback", options =>
{
    options.Secret = config["HermesAgent:WebhookSecret"];
    options.AllowedEventTypes = ["chat.completed", "run.completed"];
    options.OnCompletion = async ctx =>
    {
        // 处理回调事件
    };
});
```

---

## 依赖关系

```
HermesAgent.Sdk.AspNetCore
    └── HermesAgent.Sdk (引用)
    └── Microsoft.AspNetCore.App (框架引用)

HermesAgent.Sdk
    ├── Microsoft.Extensions.Logging.Abstractions
    ├── Microsoft.Extensions.Http
    └── Microsoft.Extensions.Options.ConfigurationExtensions
```

---

## 配套示例

| 示例 | 项目类型 | 说明 |
|------|---------|------|
| [ConsoleChat](./samples/ConsoleChat) | Console App | 命令行聊天对话，演示 Chat 客户端使用 |
| [WebApiDemo](./samples/WebApiDemo) | ASP.NET Core Web API | 完整 Web API + Swagger + Webhook 接收 |
| [WebhookReceiverDemo](./samples/WebhookReceiverDemo) | ASP.NET Core | 纯 Webhook 接收端示例 |

---


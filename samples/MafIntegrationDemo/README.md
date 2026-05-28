# MafIntegrationDemo — Hermes SDK · Microsoft Agent Framework 集成示例

本示例项目演示如何通过 `HermesAgent.Sdk.MicrosoftAgent` 适配器包，将 Hermes Agent SDK 以 `IChatClient` 的形式接入 [Microsoft Agent Framework (MAF)](https://learn.microsoft.com/en-us/dotnet/ai/) / `Microsoft.Extensions.AI` 生态。

## 功能演示

| 场景 | 路由 | 说明 |
|------|------|------|
| [1] 普通对话 | `POST /v1/chat/completions` | 无工具时的对话补全，支持多轮消息历史 |
| [2] 工具调用 | `POST /v1/responses` | 检测到 Tools 时自动路由到 Responses API，演示 Function Calling |
| [3] 流式对话 | `POST /v1/chat/completions?stream=true` | SSE 流式输出，逐 token 展示 |
| [4] Run 模式 | `POST /v1/runs` + SSE | 通过 `UseHermesRun()` 中间件标记切换到 Run 模式（需服务端开启） |

## 路由策略

适配器根据 `ChatOptions.Tools` 自动选择底层 API：

```
Tools == null / empty  ──→  Chat Completions（普通对话）
Tools != null          ──→  Responses API（函数调用/工具使用）
UseHermesRun() 标记     ──→  Run + SSE（异步运行）
```

## 运行

```bash
cd samples/MafIntegrationDemo
dotnet run
```

确保 Hermes Agent 服务已启动并在 `appsettings.json` 中配置了正确的 `ApiBaseUrl`。

## 依赖

- `HermesAgent.Sdk` — 核心 SDK
- `HermesAgent.Sdk.MicrosoftAgent` — MAF 适配器
- `Microsoft.Extensions.Hosting`

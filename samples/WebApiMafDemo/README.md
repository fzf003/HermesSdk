# WebApiMafDemo

基于 `HermesAgent.Sdk.MicrosoftAgent` 的 ASP.NET Core Web API 示例，展示了如何在 Web 环境中使用 Hermes SDK 的 MAF 适配器。

## 功能特性

- ✅ `POST /api/chat` — 普通对话（Responses API）
- ✅ `POST /api/chat/stream` — SSE 流式对话
- ✅ 自动会话管理（AutoSessionMiddleware + Topic 模型）
- ✅ 依赖注入配置

## Topic 模型

会话状态由 Hermes Server 通过 `conversation` 字段自动管理，客户端只需传递一个 **Topic ID**：

- **有 Topic ID** → 服务端自动关联历史会话，实现多轮对话上下文延续
- **无 Topic ID** → 服务端创建新会话（AutoSessionMiddleware 自动生成 `topic-{Guid}`）

### 调用示例

```bash
# 首次对话（不传 conversation，服务端自动生成）
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "你好"}'

# 多轮对话（传入 topic ID 延续上下文）
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "刚才说了什么？", "conversation": "topic-xxx..."}'
```

### 流式对话

```bash
curl -X POST http://localhost:5000/api/chat/stream \
  -H "Content-Type: application/json" \
  -d '{"prompt": "讲个故事", "conversation": "topic-xxx..."}'
```

## 快速开始

### 1. 配置

编辑 `appsettings.json`，确保配置了正确的 Hermes Agent 地址和密钥：

```json
{
  "HermesAgent": {
    "ApiBaseUrl": "http://localhost:8642",
    "ApiKey": "your-api-key",
    "Timeout": "00:15:30",
    "Maf": {
      "EnableAutoSession": true
    }
  }
}
```

### 2. 运行

```bash
dotnet run
```

### 3. 调用 API

服务启动后访问 `http://localhost:5000`，使用上述 curl 命令测试。

## 配置选项

| 选项 | 默认值 | 说明 |
|------|--------|------|
| `HermesAgent:Maf:EnableAutoSession` | `false` | 启用自动会话 ID 注入 |
| `HermesAgent:Maf:EnableRunMiddleware` | `false` | 启用 Run 中间件 |
| `HermesAgent:Maf:EnableOpenTelemetry` | `false` | 启用 OpenTelemetry 支持 |

## 相关项目

- [HermesAgent.Sdk.MicrosoftAgent](../../src/HermesAgent.Sdk.MicrosoftAgent/) — MAF 适配器源码
- [MafIntegrationDemo](../MafIntegrationDemo/) — 控制台示例

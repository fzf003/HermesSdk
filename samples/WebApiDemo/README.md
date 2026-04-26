# WebApiDemo 示例

这是一个使用 HermesAgent.Sdk 的 ASP.NET Core Web API 示例应用程序。

## 功能特性

- ✅ RESTful API 设计
- ✅ 聊天对话 API（同步和流式）
- ✅ 作业管理 API（创建、查询）
- ✅ 运行管理 API（创建、事件订阅）
- ✅ Swagger/OpenAPI 文档
- ✅ 依赖注入配置
- ✅ 结构化日志

## 快速开始

### 1. 配置 HermesAgent

本项目采用**配置分离策略**，符合 .NET 最佳实践：

#### 公开配置 (`appsettings.json`)

非敏感配置已包含在 `appsettings.json` 中，可以直接提交到版本控制：

```json
{
  "HermesAgent": {
    "ApiBaseUrl": "http://localhost:8642",
    "Timeout": "00:00:30"
  }
}
```

#### 敏感配置 (`.env` 文件)

敏感信息（如 API Key）应放在 `.env` 文件中，该文件已被 `.gitignore` 排除，不会提交到版本控制。

在项目根目录创建 `.env` 文件：

```
# HermesAgent 敏感配置
HermesAgent__ApiKey=your-actual-api-key
HermesAgent__WebhookSecret=your-webhook-secret
```

**说明**：
- 使用双下划线 `__` 作为层级分隔符，这是 ASP.NET Core 环境变量绑定的标准格式
- 仅在 `Development` 环境下，应用启动时会自动加载 `.env` 文件中的配置
- 环境变量优先级高于 `appsettings.json`，允许灵活覆盖配置

### 2. 运行应用程序

```bash
dotnet run
```

### 3. 访问 API 文档

打开浏览器访问：`https://localhost:5001/swagger`

## API 端点

### 聊天 API

#### POST `/api/chat`
同步聊天接口，返回完整的响应。

**请求示例：**
```json
{
  "messages": [
    {
      "role": "user",
      "content": "你好，请介绍一下自己"
    }
  ],
  "options": {
    "model": "gpt-4",
    "temperature": 0.7,
    "maxTokens": 1000
  }
}
```

#### POST `/api/chat/stream`
流式聊天接口，实时返回响应内容。

**请求示例：** 同上，但会以流式方式返回内容。

### 作业管理 API

#### POST `/api/jobs`
创建新作业。

#### GET `/api/jobs/{jobId}`
获取作业详情。

### 运行管理 API

#### POST `/api/runs`
创建新运行。

#### GET `/api/runs/{runId}/events`
订阅运行事件（Server-Sent Events）。

## 使用场景

- **Web 应用程序后端**: 为前端应用提供 AI 聊天服务
- **API 服务**: 构建基于 Hermes Agent 的 SaaS 服务
- **微服务架构**: 在微服务中集成 AI 功能
- **API 网关**: 作为 AI 服务的代理层

## 配置选项

### 公开配置 (`appsettings.json`)

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `HermesAgent:ApiBaseUrl` | API 基础地址 | `http://localhost:8642` |
| `HermesAgent:Timeout` | 请求超时时间 | `00:00:30` |
| `HermesAgent:DefaultModel` | 默认模型名称 | `default` |
| `HermesAgent:EnableRequestLogging` | 是否启用请求日志 | `true` |

### 敏感配置 (`.env` 或环境变量)

| 配置项 | 说明 | 必需 |
|--------|------|------|
| `HermesAgent__ApiKey` | API 密钥 | 是 |
| `HermesAgent__WebhookSecret` | Webhook 签名密钥 | 否 |

**配置优先级** (从高到低)：
1. 命令行参数 (如 `--HermesAgent:ApiKey=xxx`)
2. 环境变量 (包括 `.env` 文件加载的)
3. `appsettings.{Environment}.json`
4. `appsettings.json`
5. 代码默认值

## 测试示例

### 使用 curl 测试聊天 API

```bash
curl -X POST "https://localhost:5001/api/chat" \
     -H "Content-Type: application/json" \
     -d '{
       "messages": [{"role": "user", "content": "你好"}],
       "options": {"model": "gpt-4", "temperature": 0.7}
     }'
```

### 使用 curl 测试流式聊天

```bash
curl -X POST "https://localhost:5001/api/chat/stream" \
     -H "Content-Type: application/json" \
     -d '{
       "messages": [{"role": "user", "content": "讲个笑话"}],
       "options": {"model": "gpt-4", "temperature": 0.7, "stream": true}
     }'
```

## 相关链接

- [HermesAgent.Sdk 文档](../../docs/)
- [API 参考](../../docs/api-reference.md)
- [Swagger 文档](https://localhost:5001/swagger)（运行后访问）
# ConsoleChat 示例

这是一个使用 HermesAgent.Sdk 的控制台聊天应用程序示例。

## 功能特性

- ✅ 同步聊天对话
- ✅ 流式聊天对话（实时输出）
- ✅ 依赖注入配置
- ✅ appsettings.json 配置支持
- ✅ User Secrets 支持（安全存储 API Key）

## 快速开始

### 1. 配置 API Key

编辑 `appsettings.json` 或使用 User Secrets：

```bash
# 使用 User Secrets（推荐）
dotnet user-secrets set "HermesAgent:ApiKey" "your-actual-api-key"
```

### 2. 运行程序

```bash
dotnet run
```

### 3. 使用示例

```
🤖 Hermes Agent 控制台聊天示例
输入 'exit' 退出，输入 'stream' 切换到流式模式
--------------------------------

你: 你好，请介绍一下自己
AI: 你好！我是 Hermes Agent，一个强大的 AI 助手...

你: stream
流式模式: 开启

你: 请写一首关于编程的诗
AI: 在代码的海洋中航行
键盘敲击出梦想的旋律...
```

## 代码说明

### 主要组件

- `Program.cs`: 主程序，演示同步和流式聊天
- `appsettings.json`: 配置文件
- `ConsoleChat.csproj`: 项目文件

### 使用场景

- **开发调试**: 快速测试聊天功能
- **命令行工具**: 构建简单的 AI 助手工具
- **学习示例**: 了解 SDK 的基本用法

## 配置选项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `HermesAgent:BaseUrl` | API 基础地址 | `https://api.hermes-agent.com` |
| `HermesAgent:ApiKey` | API 密钥 | 必需 |
| `HermesAgent:Timeout` | 请求超时时间 | `00:00:30` |

## 相关链接

- [HermesAgent.Sdk 文档](../../docs/)
- [API 参考](../../docs/api-reference.md)
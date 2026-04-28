# ConsoleRuns 示例

使用 HermesAgent.Sdk Runs API 的控制台交互式演示程序。

## 功能特性

- ✅ **SSE 实时事件流** — 彩色事件类型 + emoji 图标实时展示 Agent 运行过程
- ✅ **RunAndWait 阻塞结果** — 等待动画 + 完整结果输出
- ✅ **RunWithLogging 日志** — 通过 ILogger 输出结构化运行日志
- ✅ **批量启动** — 逗号分隔多 prompt，并发 StartAsync 收集 runId

## 快速开始

### 1. 配置 API Key

编辑 `appsettings.json`：

```json
{
  "HermesAgent": {
    "ApiBaseUrl": "http://localhost:8642",
    "ApiKey": "your-actual-api-key"
  }
}
```

### 2. 运行程序

```bash
cd samples/ConsoleRuns
dotnet run
```

### 3. 使用示例

```
🤖 Hermes Agent Runs API 演示
════════════════════════════════
  1. SSE 实时事件流
  2. RunAndWait 阻塞等待结果
  3. RunWithLogging 结构化日志
  4. 批量启动 (收集 runId)
  5. 退出
════════════════════════════════
请选择 [1-5]:
```

#### 模式 1 — SSE 实时事件流

```
📡 订阅事件流: 分析项目性能瓶颈
────────────────────────────────────────────────────────────
12:00:01  🚀 run_started    模型: qwen3.5-plus
12:00:02  🔧 tool_started   read_file
12:00:05  ✅ tool_completed read_file (耗时 320ms)
12:00:08  💡 reasoning      发现 3 处可能的性能瓶颈...
12:00:15  🎯 completion     分析完成，建议优化...
────────────────────────────────────────────────────────────
✅ 事件流结束
```

#### 模式 2 — RunAndWait

```
⏳ 正在运行: "审查代码安全性"
⏳ 等待中 /  (旋转动画)

✅ 运行完成！
──────────────────────────────────────────────────
状态: completed
耗时: 34000ms
工具调用: 6 次
──────────────────────────────────────────────────
输出:
安全审查完成，发现 2 个低危问题...
```

#### 模式 3 — RunWithLogging

```
info: Program[0]
      🚀 运行已启动 (run_abc123)
info: Program[0]
      🔧 正在执行: read_file
info: Program[0]
      ✅ 运行完成
```

#### 模式 4 — 批量启动

```
🚀 批量启动 3 个任务...
────────────────────────────────────────────────────────────
  [1] runId: run_001 ← 分析项目性能瓶颈
  [2] runId: run_002 ← 审查代码安全性
  [3] runId: run_003 ← 检查依赖版本兼容性
────────────────────────────────────────────────────────────
✅ 全部 3 个任务已启动！

💡 提示: 使用 BatchRuns 示例项目可监控这些任务的实时进度。
```

## 配置选项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `HermesAgent:ApiBaseUrl` | API 基础地址 | `http://localhost:8642` |
| `HermesAgent:ApiKey` | API 密钥 | 必需 |
| `HermesAgent:Timeout` | 请求超时时间 | `00:10:00` |

## 相关链接

- [HermesAgent.Sdk 文档](../../document/)
- [BatchRuns 示例](../BatchRuns/) — 批处理进度监控

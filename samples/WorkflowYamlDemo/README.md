# WorkflowYamlDemo 示例

演示 HermesAgent.Sdk.WorkflowChain 的 **YAML 声明式工作流**功能——Phase 4 核心特性。

## 功能特性

- ✅ YAML 声明式工作流加载与执行（`RegisterFromYamlAsync`）
- ✅ 重试策略演示（`exponential_backoff` / `fixed_interval`）
- ✅ 超时策略演示（`timeout: 5s` + `timeout_action: fail / skip`）
- ✅ 错误策略演示（`SkipFailedBranch` — 并行分支失败不终断工作流）
- ✅ 变量解析演示（`{{steps.x.output.field}}` 模板替换）
- ✅ 热重载演示（文件监控 + 自动重新注册）
- ✅ 语义版本管理（解析、比较、最新版本查询）
- ✅ 交互式菜单（无需查 API 文档，启动即用）

## 快速开始

### 1. 配置 API（可选）

本 demo 使用 Mock 步骤，**不需要真实的 Hermes Agent Server**。如需连接真实 Agent，编辑 `appsettings.json`：

```json
{
  "HermesAgent": {
    "ApiBaseUrl": "http://your-agent-server:8642",
    "ApiKey": "your-api-key"
  }
}
```

### 2. 运行程序

```bash
dotnet run
```

### 3. 菜单选项

```
🤖 HermesAgent YAML 声明式工作流演示
════════════════════════════════════
  1. YAML 工作流加载与执行
  2. 重试策略演示
  3. 超时策略演示
  4. 错误策略演示
  5. 变量解析演示
  6. 热重载演示
  7. 版本管理演示
  8. 退出
════════════════════════════════════
请选择 [1-8]:
```

### 4. 各选项预期输出

| 选项 | 演示内容 | 预期输出 |
|------|---------|---------|
| 1 | 基础 YAML 工作流 | 解析成功 → 注册 → 启动 → 步骤状态 `completed` |
| 2 | Retry 配置 | FailingStep 失败 → 引擎按 `exponential_backoff` 重试 |
| 3 | Timeout 配置 | DelayStep 超过 5s → 触发 `TimeoutAction.Fail` |
| 4 | ErrorPolicy | 并行分支失败 → `SkipFailedBranch` → 其他分支继续 |
| 5 | 变量解析 | `{{steps.fetch.output.data}}` → 替换为实际值 |
| 6 | 热重载 | 编辑临时目录中的 YAML 文件 → 自动重新注册 |
| 7 | 版本管理 | 解析 `2.0.1-beta` → 注册多版本 → 查询最新版本 |

## 代码说明

- **Program.cs**：主程序，包含所有步骤处理器子类和演示逻辑
- **appsettings.json**：配置文件（HermesAgent 连接信息）
- **WorkflowYamlDemo.csproj**：项目文件

## 相关链接

- [HermesAgent.Sdk.WorkflowChain 源码](../../src/HermesAgent.Sdk.WorkflowChain/)
- [WorkflowChainDemo](../WorkflowChainDemo/) — 编程式工作流演示
- [Phase 4 变更设计](../../openspec/changes/phase4-architecture-enhancement/design.md)

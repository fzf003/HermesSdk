# WorkflowApprovalDemo

人工审批 + Agent 混合工作流的 Web API 示例项目。

演示 WorkflowChain 的审批能力：
- **ReviewAgentStep** (Agent RunClient) → **ManagerApprovalStep** (人工审批) → **NotifyAgentStep** / **EscalationAgentStep**
- 审批通过 → 通知步骤
- 审批拒绝 → 升级步骤

## 前置条件

- .NET 10.0 SDK
- Hermes Agent 服务运行在 `http://localhost:8642`（用于 Agent RunClient 模式）

## 运行

```bash
cd samples/WorkflowApprovalDemo
dotnet run
```

启动后访问：
- Swagger UI: `http://localhost:5000/swagger`
- API 基址: `http://localhost:5000/api/workflows`

## 心跳超时机制

本项目心跳阈值设为 **5 分钟**（Agent 步骤合理超时），但 `ManagerApprovalStep` 继承 `HumanApprovalStepHandler`，其默认 `HeartbeatExtension = 24 小时`。

心跳检测自动计算每个实例的有效阈值：
- 仅 Agent 步骤在 InFlight → 使用全局 5 分钟阈值
- 审批步骤在 InFlight → 有效阈值为 `max(5 分钟, 24 小时) = 24 小时`

超时后实例状态为 `timed-out`（非 `failed`），可通过以下方式恢复：
1. **自动恢复**：审批回调/Webhook 回调到达时自动恢复为 `running`
2. **手动恢复**：调用 `POST /api/workflows/{instanceId}/resume`

## API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/workflows` | 启动工作流 |
| `GET` | `/api/workflows/{instanceId}` | 查询实例状态 |
| `GET` | `/api/workflows/{instanceId}/steps` | 查询步骤记录 |
| `POST` | `/api/workflows/{instanceId}/approval/callback` | 人工审批回调 |
| `POST` | `/api/workflows/{instanceId}/webhook/callback` | Agent Webhook 回调 |
| `POST` | `/api/workflows/{instanceId}/resume` | 恢复超时实例 |

## 实例状态

| 状态 | 说明 |
|------|------|
| `running` | 正常运行中 |
| `timed-out` | 心跳超时，疑似故障待确认（可恢复） |
| `completed` | 全部步骤完成 |
| `failed` | 确认故障（终态） |

## 使用流程

### 1. 启动工作流

```http
POST /api/workflows
Content-Type: application/json

{
  "entryStepId": "review-agent",
  "initialInput": {
    "title": "采购申请 #P2026-0501",
    "amount": 25000,
    "department": "研发部",
    "requester": "李四"
  }
}
```

响应：
```json
{
  "instanceId": "wf-abc123",
  "status": "running"
}
```

> 启动后，ReviewAgentStep 通过 RunClient 请求 Hermes Agent 审核。Agent 返回结果后，ManagerApprovalStep 进入 `Dispatched` 状态等待人工审批。此时心跳有效阈值自动提升为 24 小时（因审批步骤在 InFlight）。

### 2. 查询实例状态

```http
GET /api/workflows/wf-abc123
```

响应（运行中）：
```json
{
  "instanceId": "wf-abc123",
  "status": "running",
  "entryStepId": "review-agent",
  "isRunning": true,
  "activeSteps": ["manager-approval"]
}
```

响应（超时后）：
```json
{
  "instanceId": "wf-abc123",
  "status": "timed-out",
  "entryStepId": "review-agent",
  "isRunning": true,
  "activeSteps": []
}
```

### 3. 查询步骤记录

```http
GET /api/workflows/wf-abc123/steps
```

响应：
```json
[
  { "stepId": "review-agent", "stepType": "Agent", "status": "Completed", "duration": 3200 },
  { "stepId": "manager-approval", "stepType": "Approval", "status": "Dispatched", "outputSnapshot": null }
]
```

### 4. 人工审批回调

**审批通过：**
```http
POST /api/workflows/wf-abc123/approval/callback
Content-Type: application/json

{
  "stepId": "manager-approval",
  "decision": "approved",
  "comment": "金额合理，批准采购",
  "approverId": "mgr-zhangsan"
}
```

→ 工作流推进到 `notify-step` → `completed`

**审批拒绝：**
```http
POST /api/workflows/wf-abc123/approval/callback
Content-Type: application/json

{
  "stepId": "manager-approval",
  "decision": "rejected",
  "comment": "金额过大，需要总监审批",
  "approverId": "mgr-zhangsan"
}
```

→ 工作流推进到 `escalation-step` → `completed`

### 6. 恢复超时实例

当实例因心跳超时进入 `timed-out` 状态时，可通过 resume 端点恢复：

```http
POST /api/workflows/wf-abc123/resume
```

响应（恢复成功）：
```json
{
  "instanceId": "wf-abc123",
  "status": "running",
  "message": "超时实例已恢复"
}
```

响应（实例非 timed-out 状态）：
```json
{
  "error": "实例状态不是 timed-out: running"
}
```

> **注意**：审批回调到达时，如果实例为 `timed-out` 状态，引擎会自动恢复为 `running` 并继续执行回调逻辑，无需手动 resume。

### 7. Agent Webhook 回调（可选）

如果 Agent 使用 Webhook 模式而非 RunClient，可通过此端点模拟回调：

```http
POST /api/workflows/wf-abc123/webhook/callback
Content-Type: application/json

{
  "stepId": "review-agent",
  "output": "{\"approved\":true,\"comment\":\"审核通过\"}"
}
```

## 项目结构

```
WorkflowApprovalDemo/
├── Program.cs                    # WebApplication + Minimal API + Swagger
├── Steps/
│   ├── ReviewAgentStep.cs        # Agent 审核步骤 (RunClient)
│   ├── ManagerApprovalStep.cs    # 人工审批步骤 (HumanApproval)
│   ├── NotifyAgentStep.cs        # 通知步骤 (CodeStep)
│   ├── EscalationAgentStep.cs    # 升级步骤 (CodeStep)
│   └── OutMessage.cs             # 共享 record
├── Models/
│   ├── StartWorkflowRequest.cs
│   ├── ApprovalCallbackRequest.cs
│   ├── WebhookCallbackRequest.cs
│   ├── WorkflowInstanceResponse.cs
│   └── StepRecordResponse.cs
├── appsettings.json
└── WorkflowApprovalDemo.csproj
```

## 架构说明

WorkflowEngine 作为 Singleton 注册在 DI 中，通过 `IHostedService` 长驻后台：

- **WorkflowEngineInitializationService** — 应用启动时自动恢复持久化中的运行实例
- **WorkflowHeartbeatService** — 每分钟扫描超时实例（使用动态阈值：Agent 步骤用全局 5 分钟，审批步骤用 24 小时扩展）

API 端点通过 DI 注入 `WorkflowEngine`，调用其公开方法操作工作流。

超时恢复机制：
- 心跳超时 → 实例标记为 `timed-out`（非 `failed`），活跃步骤标记 `Failed`（ErrorMessage = "心跳超时"）
- 审批/Webhook 回调到达 → 自动恢复为 `running`，继续执行
- `ResumeTimedOutWorkflowAsync` → 手动恢复，重新 Dispatch 超时步骤

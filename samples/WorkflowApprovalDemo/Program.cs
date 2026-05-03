using HermesAgent.Sdk.Extensions;
using HermesAgent.Sdk.WorkflowChain;
using HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Models;
using HermesAgent.Sdk.WorkflowChain.ApprovalDemo.Steps;

var builder = WebApplication.CreateBuilder(args);

// ── 服务注册 ──
builder.Services.AddHermesAgent(builder.Configuration);
builder.Services.AddWorkflowChain(chain =>
{
    chain.AddSqliteStateStore("Data Source=workflow-approval-demo.db");
    chain.SetHeartbeatThreshold(TimeSpan.FromMinutes(5));
    // ManagerApprovalStep 继承 HumanApprovalStepHandler，默认 HeartbeatExtension = 24 小时
    // 心跳检测自动计算有效阈值：Agent 步骤用全局 5 分钟，审批步骤用 24 小时

    chain.AddStep<ReviewAgentStep>();
    chain.AddStep<ManagerApprovalStep>();
    chain.AddStep<NotifyAgentStep>();
    chain.AddStep<EscalationAgentStep>();    
});

// ── Swagger ──
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "WorkflowApprovalDemo API", Version = "v1" });
});

var app = builder.Build();

// ── Swagger 中间件（Demo 项目始终启用） ──
app.UseSwagger();
app.UseSwaggerUI();

// =================================================================
// Minimal API 端点
// =================================================================

/// <summary>启动工作流</summary>
app.MapPost("/api/workflows", async (StartWorkflowRequest request, WorkflowEngine engine) =>
{
    var context = new WorkflowContext
    {
        InstanceId = request.InstanceId ?? $"wf-{Guid.NewGuid():N}"[..20],
        InitialInput = request.InitialInput ?? new Dictionary<string, object?>(),
    };

    var instanceId = await engine.StartAsync(request.EntryStepId, context);
    var instance = engine.GetInstance(instanceId);

    return Results.Ok(new
    {
        InstanceId = instanceId,
        Status = instance?.Status ?? "running"
    });
})
.WithName("StartWorkflow")
.WithTags("Workflows");

/// <summary>查询工作流实例状态</summary>
app.MapGet("/api/workflows/{instanceId}", (string instanceId, WorkflowEngine engine) =>
{
    var instance = engine.GetInstance(instanceId);
    if (instance == null)
        return Results.NotFound(new { Error = $"实例 {instanceId} 不存在" });

    var activeSteps = instance
        .GetStepRecords()
        .Where(r => r.Status is StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running)
        .Select(r => r.StepId)
        .ToList();

    return Results.Ok(new WorkflowInstanceResponse
    {
        InstanceId = instance.Context.InstanceId,
        Status = instance.Status,
        EntryStepId = instance.EntryStepId,
        IsRunning = instance.Status is "running" or "timed-out",
        ActiveSteps = activeSteps
    });
})
.WithName("GetWorkflow")
.WithTags("Workflows");

/// <summary>查询步骤执行记录</summary>
app.MapGet("/api/workflows/{instanceId}/steps", (string instanceId, WorkflowEngine engine) =>
{
    var instance = engine.GetInstance(instanceId);
    if (instance == null)
        return Results.NotFound(new { Error = $"实例 {instanceId} 不存在" });

    var records = engine.GetStepRecords(instanceId);
    var response = records.Select(r => new StepRecordResponse
    {
        StepId = r.StepId,
        StepType = r.StepType,
        Status = r.Status.ToString(),
        StartedAt = r.StartedAt != default ? r.StartedAt.ToString("O") : null,
        Duration = r.Duration?.TotalMilliseconds,
        OutputSnapshot = r.OutputSnapshot,
        ErrorMessage = r.ErrorMessage
    }).ToList();

    return Results.Ok(response);
})
.WithName("GetWorkflowSteps")
.WithTags("Workflows");

/// <summary>人工审批回调</summary>
app.MapPost("/api/workflows/{instanceId}/approval/callback",
    async (string instanceId, ApprovalCallbackRequest request, WorkflowEngine engine) =>
{
    await engine.OnHumanApprovalCallbackAsync(
        instanceId: instanceId,
        stepId: request.StepId,
        decision: request.Decision,
        comment: request.Comment,
        approverId: request.ApproverId
    );

    return Results.Ok(new { Processed = true, InstanceId = instanceId, Decision = request.Decision });
})
.WithName("ApprovalCallback")
.WithTags("Callbacks");

/// <summary>Agent Webhook 回调</summary>
app.MapPost("/api/workflows/{instanceId}/webhook/callback",
    async (string instanceId, WebhookCallbackRequest request, WorkflowEngine engine) =>
{
    await engine.OnWebhookCallbackAsync(
        instanceId: instanceId,
        completedStepId: request.StepId,
        output: request.Output,
        error: request.Error
    );

    return Results.Ok(new { Processed = true, InstanceId = instanceId });
})
.WithName("WebhookCallback")
.WithTags("Callbacks");

/// <summary>恢复超时工作流实例</summary>
app.MapPost("/api/workflows/{instanceId}/resume",
    async (string instanceId, WorkflowEngine engine) =>
{
    try
    {
        await engine.ResumeTimedOutWorkflowAsync(instanceId);
        var instance = engine.GetInstance(instanceId);
        return Results.Ok(new
        {
            InstanceId = instanceId,
            Status = instance?.Status ?? "running",
            Message = "超时实例已恢复"
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
.WithName("ResumeWorkflow")
.WithTags("Workflows");

app.Run();

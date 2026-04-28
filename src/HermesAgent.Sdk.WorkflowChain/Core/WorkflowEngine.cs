using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流引擎 — 调度器。
/// 职责链模式的运行时：管理实例、调度步骤、处理回调、追踪执行轨迹。
/// </summary>
public class WorkflowEngine
{
    private readonly Dictionary<string, IStepHandler> _handlers;
    private readonly IHermesWebhookClient _webhookClient;
    private readonly IHermesRunClient _runClient;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly IWorkflowStateStore _stateStore;
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    // ParallelJoin 计数器：instanceId → (JoinDownstreamStepId → 剩余计数, 原始子步骤列表)
    private readonly ConcurrentDictionary<string, JoinTracker> _joinTrackers = new();

    public WorkflowEngine(
        IEnumerable<IStepHandler> handlers,
        IHermesWebhookClient webhookClient,
        IHermesRunClient runClient,
        ILogger<WorkflowEngine> logger,
        IWorkflowStateStore stateStore)
    {
        _handlers = handlers.ToDictionary(h => h.StepId);
        _webhookClient = webhookClient;
        _runClient = runClient;
        _logger = logger;
        _stateStore = stateStore;
    }

    // =================================================================
    // 公开 API
    // =================================================================

    /// <summary>
    /// 启动工作流。
    /// </summary>
    /// <param name="entryStepId">入口步骤 ID</param>
    /// <param name="context">工作流上下文（InstanceId 可选，不指定则自动生成）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工作流实例 ID</returns>
    public async Task<string> StartAsync(
        string entryStepId,
        WorkflowContext context,
        CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(entryStepId, out var entryHandler))
            throw new ArgumentException($"入口步骤未注册: {entryStepId}");

        var instance = new WorkflowInstance
        {
            Context = context,
            EntryStepId = entryStepId,
        };

        // 为所有已注册步骤预建 Pending 记录
        InitializeRecords(instance);

        _instances[context.InstanceId] = instance;

        _logger.LogInformation("工作流启动: {InstanceId}, 入口: {Step}, 总步骤: {Count}",
            context.InstanceId, entryStepId, _handlers.Count);

        // 持久化：新实例
        await SaveCheckpointAsync(instance, ct);

        // 执行入口步骤
        await ExecuteStepAsync(instance, entryStepId, triggeredBy: null, ct);

        return context.InstanceId;
    }

    /// <summary>
    /// 处理 Webhook 回调 — Hermes Agent 处理完成后回调 .NET 应用。
    /// </summary>
    public async Task OnWebhookCallbackAsync(
        string instanceId,
        string completedStepId,
        string? output,
        string? error,
        CancellationToken ct = default)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            _logger.LogWarning("收到未知实例回调: {InstanceId}", instanceId);
            return;
        }

        var ctx = instance.Context;

        // 幂等防护 — 原子性地检查并声明处理权，防止并发重复回调
        StepRecord record;
        lock (instance.SyncLock)
        {
            record = GetOrCreateRecord(instance, completedStepId);
            if (record.Status is StepStatus.Completed or StepStatus.Failed)
            {
                _logger.LogWarning(
                    "幂等防护: {InstanceId}/{StepId} 已被处理 (Status={Status}), 忽略重复回调",
                    instanceId, completedStepId, record.Status);
                return;
            }
        }

        // 记录输出（锁内更新共享集合）
        lock (ctx.SyncLock)
        {
            ctx.StepOutputs[completedStepId] = output;
            ctx.PendingStepIds.Remove(completedStepId);
        }
        lock (instance.SyncLock)
        {
            instance.InFlightStepIds.Remove(completedStepId);
        }
        record.OutputSnapshot = output;

        // 更新追踪记录
        if (!string.IsNullOrEmpty(error))
        {
            MarkFailed(record, error, detail: null);
            ctx.IsRunning = false;
            _logger.LogError("步骤失败: {InstanceId}/{Step}, 错误: {Error}",
                instanceId, completedStepId, error);
        }
        else
        {
            MarkCompleted(record);
            _logger.LogInformation("步骤回调: {InstanceId}/{Step}, 耗时: {Duration}ms, 待完成: {Pending}",
                instanceId, completedStepId,
                record.Duration?.TotalMilliseconds ?? 0,
                ctx.PendingStepIds.Count);
        }

        // 持久化：回调处理
        await SaveCheckpointAsync(instance, ct);

        // 检查 ParallelJoin 计数（Webhook 回调完成也需递减计数器）
        if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, completedStepId, ct))
            return;

        // 如果还有并行步骤未完成，继续等待
        var pendingCount = 0;
        lock (ctx.SyncLock)
            pendingCount = ctx.PendingStepIds.Count;
        if (pendingCount > 0)
        {
            _logger.LogDebug("并行步骤尚未全部完成，继续等待: {Remaining}", pendingCount);
            return;
        }

        // 如果工作流已终止，记录跳过下游步骤
        if (!ctx.IsRunning)
        {
            _logger.LogWarning("工作流已终止: {InstanceId}，不再推进", instanceId);
            return;
        }

        // 找到 Handler 决定下一步
        if (!_handlers.TryGetValue(completedStepId, out var handler))
        {
            _logger.LogError("未找到步骤处理器: {Step}", completedStepId);
            return;
        }

        var result = await handler.ExecuteAsync(ctx, ct);

        // 记录触发了哪些步骤
        if (record != null && result.NextStepIds is { Count: > 0 })
        {
            record.TriggeredSteps = new List<string>(result.NextStepIds);
        }

        if (!result.IsSuccess || !ctx.IsRunning)
        {
            _logger.LogWarning("工作流终止: {InstanceId}, 原因: {Reason}",
                instanceId, result.Error?.Message ?? "IsRunning=false");
            instance.Status = "failed";

            await SaveCheckpointAsync(instance, ct);
            return;
        }

        // 推进下一步
        await AdvanceAsync(instance, result, completedStepId, ct);
    }

    /// <summary>获取工作流实例</summary>
    public WorkflowInstance? GetInstance(string instanceId)
        => _instances.TryGetValue(instanceId, out var inst) ? inst : null;

    /// <summary>获取实例的所有步骤记录（按 StartedAt 排序）</summary>
    public IReadOnlyList<StepRecord> GetStepRecords(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return Array.Empty<StepRecord>();

        return instance.StepRecords
            .OrderBy(r => r.StartedAt == default ? DateTime.MaxValue : r.StartedAt)
            .ThenBy(r => r.StepId)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 生成时间线摘要 — 用于快速排查问题。
    ///
    /// 输出示例:
    ///   [14:03:01.200] fetch-code       CodeStep    Completed   0.5s
    ///   [14:03:01.700] static-analysis  AgentStep   Completed   5.2s
    ///   [14:03:01.700] test-analysis    AgentStep   Failed      3.1s  ERROR: invalid JSON
    ///   [14:03:04.800] summary          CodeStep    Pending      —     upstream failed
    ///   [14:03:04.800] notify           CodeStep    Pending      —
    /// </summary>
    public string GetTimelineSummary(string instanceId)
    {
        var records = GetStepRecords(instanceId);
        if (records.Count == 0) return "(无记录)";

        var sb = new StringBuilder();
        sb.AppendLine($"工作流实例: {instanceId}");
        sb.AppendLine(new string('-', 80));

        foreach (var r in records)
        {
            var time = r.StartedAt == default ? "--------.---" : r.StartedAt.ToString("HH:mm:ss.fff");
            var type = r.StepType.PadRight(8);
            var status = r.Status.ToString().PadRight(10);
            var duration = r.Duration.HasValue
                ? $"{r.Duration.Value.TotalSeconds:F1}s".PadRight(7)
                : "—".PadRight(7);

            var line = $"[{time}] {r.StepId.PadRight(18)} {type} {status} {duration}";
            if (!string.IsNullOrEmpty(r.ErrorMessage))
                line += $" ERROR: {r.ErrorMessage}";

            sb.AppendLine(line);
        }

        sb.AppendLine(new string('-', 80));
        return sb.ToString();
    }

    // =================================================================
    // 内部调度方法
    // =================================================================

    private async Task ExecuteStepAsync(
        WorkflowInstance instance, string stepId, string? triggeredBy, CancellationToken ct)
    {
        var ctx = instance.Context;

        if (!_handlers.TryGetValue(stepId, out var handler))
        {
            _logger.LogError("未注册的步骤: {Step}", stepId);
            ctx.IsRunning = false;
            return;
        }

        var record = GetOrCreateRecord(instance, stepId);
        record.TriggeredBy = triggeredBy;

        if (handler is AgentStepHandler agentHandler)
        {
            await ExecuteAgentStepAsync(instance, stepId, agentHandler, record, ct);
        }
        else if (handler is CodeStepHandler codeHandler)
        {
            await ExecuteCodeStepAsync(instance, stepId, codeHandler, record, ct);
        }
        else if (handler is DelayStepHandler delayHandler)
        {
            await ExecuteDelayStepAsync(instance, stepId, delayHandler, record, ct);
        }
        else
        {
            _logger.LogError("不支持的步骤处理器类型: {Type}", handler.GetType().Name);
            record.Status = StepStatus.Failed;
            record.ErrorMessage = $"不支持的类型: {handler.GetType().Name}";
            ctx.IsRunning = false;
            await SaveCheckpointAsync(instance, ct);
        }
    }

    private async Task ExecuteAgentStepAsync(
        WorkflowInstance instance, string stepId, AgentStepHandler agentHandler,
        StepRecord record, CancellationToken ct)
    {
        var ctx = instance.Context;

        if (agentHandler.Mode == AgentCommunicationMode.RunClient)
        {
            // RunClient SSE 模式
            record.Status = StepStatus.Running;
            record.StartedAt = DateTime.UtcNow;

            var prompt = agentHandler.BuildPrompt(ctx);
            record.InputSnapshot = prompt;

            try
            {
                var runId = await _runClient.StartAsync(prompt, agentHandler.RunOptions, ct);
                _logger.LogDebug("RunClient 已启动: {InstanceId}/{Step}, RunId: {RunId}",
                    ctx.InstanceId, stepId, runId);

                await foreach (var evt in _runClient.SubscribeEventsAsync(runId, ct))
                {
                    if (evt is { Type: "RunCompleted" })
                    {
                        lock (ctx.SyncLock)
                            ctx.StepOutputs[stepId] = evt.Data;
                        record.OutputSnapshot = evt.Data?.ToString();
                        MarkCompleted(record);
                        break;
                    }
                }

                await SaveCheckpointAsync(instance, ct);

                // 检查 ParallelJoin：若为并行子步骤，递减计数器而非自行推进
                if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                    return;

                // 推进下一步
                var result = await agentHandler.ExecuteAsync(ctx, ct);
                record.TriggeredSteps = result.NextStepIds?.ToList();
                await AdvanceAsync(instance, result, stepId, ct);
            }
            catch (Exception ex)
            {
                MarkFailed(record, ex.Message, ex.ToString());
                record.FullStackTrace = ex.ToString();
                ctx.IsRunning = false;
                await SaveCheckpointAsync(instance, ct);
            }
        }
        else
        {
            // Webhook 模式
            record.Status = StepStatus.Dispatched;
            record.StartedAt = DateTime.UtcNow;

            var payload = BuildWebhookPayload(ctx, stepId, agentHandler);
            record.InputSnapshot = payload;

            lock (ctx.SyncLock)
                ctx.PendingStepIds.Add(stepId);
            lock (instance.SyncLock)
                instance.InFlightStepIds.Add(stepId);

            await SaveCheckpointAsync(instance, ct);

            try
            {
                var webhookResult = await _webhookClient.SendRawAsync(
                    agentHandler.RouteName,
                    agentHandler.EventType,
                    payload,
                    ct: ct);

                _logger.LogDebug("Webhook 已发送: {InstanceId}/{Step}, 状态: {Status}",
                    ctx.InstanceId, stepId, webhookResult.Status);
            }
            catch (Exception ex)
            {
                MarkFailed(record, ex.Message, ex.ToString());
                lock (instance.SyncLock)
                    instance.InFlightStepIds.Remove(stepId);
                record.FullStackTrace = ex.ToString();
                _logger.LogError(ex, "Webhook 发送失败: {InstanceId}/{Step}",
                    ctx.InstanceId, stepId);
                ctx.IsRunning = false;
                await SaveCheckpointAsync(instance, ct);
            }
        }
    }

    private async Task ExecuteCodeStepAsync(
        WorkflowInstance instance, string stepId, CodeStepHandler codeHandler,
        StepRecord record, CancellationToken ct)
    {
        var ctx = instance.Context;

        record.Status = StepStatus.Running;
        record.StartedAt = DateTime.UtcNow;

        record.InputSnapshot = JsonSerializer.Serialize(new
        {
            ctx.InstanceId,
            StepOutputs = ctx.StepOutputs.Keys.ToList(),
            Data = ctx.Data.Keys.ToList(),
            stepId,
        });

        await SaveCheckpointAsync(instance, ct);

        var result = await codeHandler.ExecuteAsync(ctx, ct);
        lock (ctx.SyncLock)
            ctx.StepOutputs[stepId] = result.Output;
        record.OutputSnapshot = result.Output?.ToString();

        if (!result.IsSuccess || !ctx.IsRunning)
        {
            MarkFailed(record, result.Error?.Message ?? "IsRunning=false",
                result.Error?.ToString());
            record.FullStackTrace = result.Error?.ToString();
            ctx.IsRunning = false;
            await SaveCheckpointAsync(instance, ct);
            return;
        }

        MarkCompleted(record);

        // 检查 ParallelJoin：若为并行子步骤，递减计数器而非自行推进
        if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
            return;

        if (result.NextStepIds is { Count: > 0 })
        {
            record.TriggeredSteps = new List<string>(result.NextStepIds);
            foreach (var nextId in result.NextStepIds)
            {
                var nextRecord = GetOrCreateRecord(instance, nextId);
                nextRecord.TriggeredBy = stepId;
            }

            await SaveCheckpointAsync(instance, ct);

            await AdvanceAsync(instance, result, stepId, ct);
        }
        else
        {
            instance.Status = "completed";
            instance.CompletedAt = DateTime.UtcNow;
            await SaveCheckpointAsync(instance, ct);
        }
    }

    private async Task ExecuteDelayStepAsync(
        WorkflowInstance instance, string stepId, DelayStepHandler delayHandler,
        StepRecord record, CancellationToken ct)
    {
        var ctx = instance.Context;

        record.Status = StepStatus.Running;
        record.StartedAt = DateTime.UtcNow;
        record.InputSnapshot = $"delay={delayHandler.DelayDuration}";

        await SaveCheckpointAsync(instance, ct);

        try
        {
            var result = await delayHandler.ExecuteDelayAsync(ct);
            lock (ctx.SyncLock)
                ctx.StepOutputs[stepId] = result.Output;
            record.OutputSnapshot = result.Output?.ToString();
            MarkCompleted(record);

            // 检查 ParallelJoin：若为并行子步骤，递减计数器而非自行推进
            if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                return;

            if (result.NextStepIds is { Count: > 0 })
            {
                record.TriggeredSteps = new List<string>(result.NextStepIds);
                await SaveCheckpointAsync(instance, ct);
                await AdvanceAsync(instance, result, stepId, ct);
            }
            else
            {
                instance.Status = "completed";
                instance.CompletedAt = DateTime.UtcNow;
                await SaveCheckpointAsync(instance, ct);
            }
        }
        catch (Exception ex)
        {
            MarkFailed(record, ex.Message, ex.ToString());
            record.FullStackTrace = ex.ToString();
            ctx.IsRunning = false;
            await SaveCheckpointAsync(instance, ct);
        }
    }

    /// <summary>
    /// 推进工作流 — 根据 StepResult 决定串行/并行/ParallelJoin。
    /// </summary>
    private async Task AdvanceAsync(
        WorkflowInstance instance, StepResult result, string completedStepId, CancellationToken ct)
    {
        if (result.NextStepIds is not { Count: > 0 })
        {
            instance.Status = "completed";
            instance.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("工作流完成: {InstanceId}", instance.Context.InstanceId);
            await SaveCheckpointAsync(instance, ct);
            return;
        }

        // ParallelJoin 模式：Engine 自动管理 Join 计数
        if (result.WaitForParallelCompletion && !string.IsNullOrEmpty(result.JoinDownstreamStepId))
        {
            var tracker = new JoinTracker
            {
                JoinDownstreamStepId = result.JoinDownstreamStepId,
                TotalCount = result.NextStepIds.Count,
                RemainingCount = result.NextStepIds.Count,
            };
            _joinTrackers[instance.Context.InstanceId] = tracker;

            _logger.LogDebug("ParallelJoin 初始化: {InstanceId}, 下游={Downstream}, 子步骤={Children}",
                instance.Context.InstanceId, result.JoinDownstreamStepId, string.Join(", ", result.NextStepIds));

            var tasks = result.NextStepIds.Select(id =>
                ExecuteStepAsync(instance, id, triggeredBy: completedStepId, ct));
            await Task.WhenAll(tasks);
        }
        // 并行模式：并发启动多个子步骤
        else if (result.WaitForParallelCompletion)
        {
            lock (instance.Context.SyncLock)
                foreach (var stepId in result.NextStepIds)
                    instance.Context.PendingStepIds.Add(stepId);

            var tasks = result.NextStepIds.Select(id =>
                ExecuteStepAsync(instance, id, triggeredBy: completedStepId, ct));
            await Task.WhenAll(tasks);
        }
        // 串行模式：逐一推进
        else
        {
            if (result.NextStepIds.Count > 1)
            {
                _logger.LogWarning(
                    "串行模式下 NextStepIds 包含 {Count} 个步骤，仅推进第一个 {First}，其余被忽略: {All}",
                    result.NextStepIds.Count, result.NextStepIds[0],
                    string.Join(", ", result.NextStepIds));
            }
            await ExecuteStepAsync(instance, result.NextStepIds[0], triggeredBy: completedStepId, ct);
        }
    }

    // =================================================================
    // 持久化辅助
    // =================================================================

    private async Task SaveCheckpointAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        try
        {
            instance.LastHeartbeat = DateTime.UtcNow;
            var checkpoint = WorkflowCheckpoint.FromInstance(instance);
            await _stateStore.SaveAsync(checkpoint, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "持久化检查点失败: {InstanceId}", instance.Context.InstanceId);
            // 不抛异常 — 持久化失败不中断业务
        }
    }

    // =================================================================
    // 内部辅助方法
    // =================================================================

    /// <summary>获取或创建步骤记录（惰性初始化，线程安全）</summary>
    private StepRecord GetOrCreateRecord(WorkflowInstance instance, string stepId)
    {
        lock (instance.SyncLock)
        {
            var existing = instance.StepRecords.Find(r => r.StepId == stepId);
            if (existing != null) return existing;

            var record = new StepRecord
            {
                StepId = stepId,
                StepType = GetStepType(stepId),
                Status = StepStatus.Pending,
            };
            instance.StepRecords.Add(record);
            return record;
        }
    }

    /// <summary>启动时预建所有步骤的 Pending 记录</summary>
    private void InitializeRecords(WorkflowInstance instance)
    {
        foreach (var (stepId, handler) in _handlers)
        {
            instance.StepRecords.Add(new StepRecord
            {
                StepId = stepId,
                StepType = GetStepType(stepId),
                Status = StepStatus.Pending,
            });
        }
    }

    /// <summary>标记步骤完成</summary>
    private static void MarkCompleted(StepRecord record)
    {
        record.Status = StepStatus.Completed;
        record.CompletedAt = DateTime.UtcNow;
        record.Duration = record.StartedAt != default
            ? record.CompletedAt.Value - record.StartedAt
            : null;
    }

    /// <summary>标记步骤失败</summary>
    private static void MarkFailed(StepRecord record, string message, string? detail)
    {
        record.Status = StepStatus.Failed;
        record.CompletedAt = DateTime.UtcNow;
        record.Duration = record.StartedAt != default
            ? record.CompletedAt.Value - record.StartedAt
            : null;
        record.ErrorMessage = message;
        record.ErrorDetail = detail;
    }

    private string GetStepType(string stepId)
    {
        if (!_handlers.TryGetValue(stepId, out var handler)) return "Unknown";
        return handler switch
        {
            AgentStepHandler => "Agent",
            CodeStepHandler => "Code",
            DelayStepHandler => "Delay",
            _ => "Custom"
        };
    }

    private static string BuildWebhookPayload(WorkflowContext ctx, string stepId, AgentStepHandler handler)
    {
        var payload = new Dictionary<string, object?>
        {
            ["instanceId"] = ctx.InstanceId,
            ["stepId"] = stepId,
            ["input"] = ctx.InitialInput,
            ["previousOutputs"] = ctx.StepOutputs,
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// 尝试递减 ParallelJoin 计数器，若全部完成则推进到汇合步骤。
    /// 所有步骤完成路径（Webhook 回调 / CodeStep / DelayStep / RunClient）统一调用此方法。
    /// </summary>
    /// <returns>true 表示由 ParallelJoin 接管了后续推进，调用方应直接 return</returns>
    private async Task<bool> TryDecrementJoinAndMaybeAdvanceAsync(
        WorkflowInstance instance, string completedStepId, CancellationToken ct)
    {
        if (!_joinTrackers.TryGetValue(instance.Context.InstanceId, out var tracker))
            return false;

        var remaining = Interlocked.Decrement(ref tracker.RemainingCount);
        if (remaining > 0)
        {
            _logger.LogDebug("ParallelJoin 等待: {InstanceId}/{Step}, 剩余 {Remaining}/{Total} 个子步骤",
                instance.Context.InstanceId, completedStepId, remaining, tracker.TotalCount);
            return true; // 还没齐，调用方应 return
        }

        // 所有子步骤完成 → 推进到汇合步
        _joinTrackers.TryRemove(instance.Context.InstanceId, out _);
        var downstreamId = tracker.JoinDownstreamStepId;
        _logger.LogInformation("ParallelJoin 完成: {InstanceId} → {Downstream}",
            instance.Context.InstanceId, downstreamId);

        await ExecuteStepAsync(instance, downstreamId, triggeredBy: null, ct);
        return true;
    }

    /// <summary>
    /// ParallelJoin 追踪器 — 管理并行子步骤的完成计数。
    /// </summary>
    private class JoinTracker
    {
        public string JoinDownstreamStepId { get; init; } = "";
        public int TotalCount { get; init; }
        public int RemainingCount;
    }
}

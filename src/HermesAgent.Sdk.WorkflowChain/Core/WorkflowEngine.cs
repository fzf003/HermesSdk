using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流引擎 — 调度器。
/// 职责链模式的运行时：管理实例、调度步骤、处理回调、追踪执行轨迹。
/// </summary>
public class WorkflowEngine : IAsyncDisposable
{
    /// <summary>
    /// 步骤运行时策略合并结果。
    /// 仅包含策略与输入模板字段（timeout/retry/error_policy/prompt），
    /// 不包含拓扑字段（depends_on/next_step_id/steps/wait_mode）。
    /// 拓扑由代码定义，不受 YAML 覆盖。
    /// </summary>
    private sealed class MergedStepRuntimeConfig
    {
        public string? Timeout { get; init; }
        public string? TimeoutAction { get; init; }
        public RetryConfigYaml? Retry { get; init; }
        public string? ErrorPolicy { get; init; }
        public string? Prompt { get; init; }
        public string? SystemPrompt { get; init; }
        public string? RouteName { get; init; }
        public string? EventType { get; init; }
        public ApprovalNotificationConfig? Notification { get; init; }
        public string? HeartbeatExtension { get; init; }
    }

    private readonly Dictionary<string, IStepHandler> _handlers;
    private readonly IHermesWebhookClient _webhookClient;
    private readonly IHermesRunClient _runClient;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly IWorkflowStateStore _stateStore;
    private readonly WorkflowRegistry? _workflowRegistry;
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    // 可靠性组件
    private readonly RetryExecutor _retryExecutor;
    private readonly ErrorHandler _errorHandler;

    // ParallelJoin 计数器：instanceId → (JoinDownstreamStepId → 剩余计数, 原始子步骤列表)
    private readonly ConcurrentDictionary<string, JoinTracker> _joinTrackers = new();

    // 子工作流追踪：parentInstanceId:subWorkflowStepId → subWorkflowInstanceId
    private readonly ConcurrentDictionary<string, string> _subWorkflowMappings = new();

    // StepDefinition 映射："workflowName:stepId" → StepDefinition
    private readonly ConcurrentDictionary<string, StepDefinition> _stepDefinitions = new();

    // 恢复 Timer："instanceId:stepId" → CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _recoveryTimers = new();

    // 恢复 Timer 的异步任务引用，供 DisposeAsync 等待完成
    private readonly ConcurrentDictionary<string, Task> _recoveryTasks = new();

    // 关闭令牌：应用退出时取消，防止恢复重发操作无限阻塞
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    private readonly IReadOnlyDictionary<Type, StepHandlerDefaults> _fluentDefaults;

    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecoveryOpTimeout = TimeSpan.FromSeconds(30);

    public WorkflowEngine(
        IEnumerable<IStepHandler> handlers,
        IHermesWebhookClient webhookClient,
        IHermesRunClient runClient,
        ILogger<WorkflowEngine> logger,
        IWorkflowStateStore stateStore,
        WorkflowRegistry? workflowRegistry = null,
        IReadOnlyDictionary<Type, StepHandlerDefaults>? fluentDefaults = null
    )
    {
        _handlers = handlers.ToDictionary(h => h.StepId);
        _webhookClient = webhookClient;
        _runClient = runClient;
        _logger = logger;
        _stateStore = stateStore;
        _workflowRegistry = workflowRegistry;
        _fluentDefaults = fluentDefaults ?? new Dictionary<Type, StepHandlerDefaults>();

        // 初始化可靠性组件
        _retryExecutor = new RetryExecutor(LoggerFactory.Create(b => { }).CreateLogger<RetryExecutor>());
        _errorHandler = new ErrorHandler(LoggerFactory.Create(b => { }).CreateLogger<ErrorHandler>(), stateStore);
    }

    // =================================================================
    // 公开 API
    // =================================================================

    /// <summary>
    /// 注册工作流的步骤定义。YAML 配置中的 retry/timeout/error_policy 通过此方法传入。
    /// </summary>
    /// <param name="workflowName">工作流名称（用作命名空间隔离）</param>
    /// <param name="definitions">步骤定义列表</param>
    public void RegisterStepDefinitions(string workflowName, IEnumerable<StepDefinition> definitions)
    {
        if (string.IsNullOrEmpty(workflowName))
            throw new ArgumentNullException(nameof(workflowName));
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        foreach (var def in definitions)
            _stepDefinitions[$"{workflowName}:{def.Id}"] = def;
    }

    public void ReplaceStepDefinitions(string workflowName, IEnumerable<StepDefinition> definitions)
    {
        if (string.IsNullOrEmpty(workflowName))
            throw new ArgumentNullException(nameof(workflowName));
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        var prefix = workflowName + ":";
        var keysToRemove = _stepDefinitions.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in keysToRemove)
            _stepDefinitions.TryRemove(key, out var _);

        RegisterStepDefinitions(workflowName, definitions);
    }

    /// <summary>
    /// 获取步骤定义。无 workflowName 时返回 null。
    /// </summary>
    private StepDefinition? GetStepDefinition(string? workflowName, string stepId)
    {
        workflowName ??= "__default__";
        return _stepDefinitions.TryGetValue($"{workflowName}:{stepId}", out var def) ? def : null;
    }

    /// <summary>
    /// 合并 YAML StepDefinition 与 Handler 默认值，产生运行时生效的步骤策略。
    /// 优先级：YAML > Handler 默认值 > 引擎内建默认值。
    ///
    /// 明确排除的拓扑字段（不会进入此合并链路）：
    /// - depends_on / next_step_id / steps / wait_mode
    /// 这些字段表达流程拓扑或调度关系，不属于步骤执行策略。
    /// 代码工作流的拓扑由 StepResult 返回值定义，YAML 不参与覆盖。
    /// </summary>
    private MergedStepRuntimeConfig MergeStepRuntimeConfig(IStepHandler handler, StepDefinition? stepDef)
    {
        var baseHandler = handler as StepHandlerBase;
        var agentHandler = handler as AgentStepHandler;

        // 读取 Fluent API 配置（如有）
        _fluentDefaults.TryGetValue(handler.GetType(), out var fluent);

        return new MergedStepRuntimeConfig
        {
            Timeout = !string.IsNullOrWhiteSpace(stepDef?.Timeout) ? stepDef!.Timeout
                    : !string.IsNullOrWhiteSpace(fluent?.Timeout) ? fluent.Timeout
                    : baseHandler?.Timeout,
            TimeoutAction = !string.IsNullOrWhiteSpace(stepDef?.TimeoutAction) ? stepDef!.TimeoutAction
                          : !string.IsNullOrWhiteSpace(fluent?.TimeoutAction) ? fluent.TimeoutAction
                          : baseHandler?.TimeoutAction,
            Retry = stepDef?.Retry ?? fluent?.Retry ?? baseHandler?.Retry,
            ErrorPolicy = !string.IsNullOrWhiteSpace(stepDef?.ErrorPolicy) ? stepDef!.ErrorPolicy
                        : !string.IsNullOrWhiteSpace(fluent?.ErrorPolicy) ? fluent.ErrorPolicy
                        : baseHandler?.ErrorPolicy,
            Prompt = !string.IsNullOrWhiteSpace(stepDef?.Prompt) ? stepDef!.Prompt
                   : !string.IsNullOrWhiteSpace(fluent?.Prompt) ? fluent.Prompt
                   : agentHandler?.Prompt,
            SystemPrompt = !string.IsNullOrWhiteSpace(stepDef?.SystemPrompt) ? stepDef!.SystemPrompt
                         : !string.IsNullOrWhiteSpace(fluent?.SystemPrompt) ? fluent.SystemPrompt
                         : agentHandler?.SystemPrompt,
            RouteName = !string.IsNullOrWhiteSpace(stepDef?.RouteName) ? stepDef!.RouteName
                      : !string.IsNullOrWhiteSpace(fluent?.RouteName) ? fluent.RouteName
                      : agentHandler?.RouteName,
            EventType = !string.IsNullOrWhiteSpace(stepDef?.EventType) ? stepDef!.EventType
                      : !string.IsNullOrWhiteSpace(fluent?.EventType) ? fluent.EventType
                      : agentHandler?.EventType,
            Notification = stepDef?.Notification ?? fluent?.Notification,
            HeartbeatExtension = !string.IsNullOrWhiteSpace(stepDef?.HeartbeatExtension) ? stepDef!.HeartbeatExtension
                               : !string.IsNullOrWhiteSpace(fluent?.HeartbeatExtension) ? fluent.HeartbeatExtension
                               : baseHandler?.HeartbeatExtension?.ToString(),
        };
    }

    private static string? ResolvePromptTemplate(string? promptTemplate, WorkflowContext ctx)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
            return null;

        return new VariableResolver(ctx).Resolve(promptTemplate);
    }

    /// <summary>
    /// 解析 Agent 步骤的有效通信模式。
    /// 优先级：YAML communication_mode > Handler.Mode 虚属性。
    /// </summary>
    private static AgentCommunicationMode ResolveAgentMode(AgentStepHandler handler, StepDefinition? stepDef)
    {
        if (!string.IsNullOrWhiteSpace(stepDef?.CommunicationMode))
        {
            return stepDef.CommunicationMode.ToLowerInvariant() switch
            {
                "none" => AgentCommunicationMode.None,
                "webhook" => AgentCommunicationMode.Webhook,
                "run_client" => AgentCommunicationMode.RunClient,
                _ => handler.Mode,
            };
        }
        return handler.Mode;
    }

    /// <summary>
    /// 启动工作流。
    /// </summary>
    /// <param name="entryStepId">入口步骤 ID</param>
    /// <param name="context">工作流上下文（InstanceId 可选，不指定则自动生成）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="workflowName">工作流名称（用于查找 StepDefinition，可选）</param>
    /// <returns>工作流实例 ID</returns>
    public async Task<string> StartAsync(
        string entryStepId,
        WorkflowContext context,
        CancellationToken ct = default,
        string? workflowName = null
    )
    {
        if (!_handlers.TryGetValue(entryStepId, out var entryHandler))
            throw new ArgumentException($"入口步骤未注册: {entryStepId}");

        var instance = new WorkflowInstance { Context = context, EntryStepId = entryStepId, };

        // 关联工作流名称与定义 ID（用于查找 StepDefinition 和追溯定义）
        if (workflowName != null)
        {
            instance.WorkflowName = workflowName;
            context.WorkflowName = workflowName;

            if (_workflowRegistry != null && _workflowRegistry.IsRegistered(workflowName))
                context.WorkflowId = _workflowRegistry.Get(workflowName).Id;
        }

        // 为工作流步骤预建 Pending 记录
        var stepCount = InitializeRecords(instance, workflowName);

        _instances[context.InstanceId] = instance;

        _logger.LogInformation(
            "工作流启动: {InstanceId}, 入口: {Step}, 总步骤: {Count}",
            context.InstanceId,
            entryStepId,
            stepCount
        );

        // 持久化：新实例
        await SaveCheckpointAsync(instance, ct);

        // 执行入口步骤
        await ExecuteStepAsync(instance, entryStepId, triggeredBy: null, ct);

        return context.InstanceId;
    }

    /// <summary>
    /// 初始化恢复 — 服务启动时从持久化存储加载 running 实例。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initializeLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            var runningIds = await _stateStore.ListRunningAsync(ct);
            _logger.LogInformation("重启恢复: 发现 {Count} 个运行中实例", runningIds.Count);

            foreach (var instanceId in runningIds)
            {
                var checkpoint = await _stateStore.LoadAsync(instanceId, ct);
                if (checkpoint == null)
                    continue;

                var instance = checkpoint.ToInstance();

                // 恢复子工作流映射
                if (checkpoint.SubWorkflowMappings != null)
                {
                    foreach (var kvp in checkpoint.SubWorkflowMappings)
                        _subWorkflowMappings[kvp.Key] = kvp.Value;
                }

                // 将 InFlight AgentStep 设为 Recovering 状态
                foreach (var stepId in instance.InFlightStepIds)
                {
                    var record = instance.StepRecords.FirstOrDefault(r => r.StepId == stepId);
                    if (record != null && record.Status == StepStatus.Dispatched)
                    {
                        record.Status = StepStatus.Recovering;

                        // 区分 Handler 类型：AgentStep 启动重发 Timer，HumanApprovalStep 静默等待
                        var handler = _handlers.GetValueOrDefault(stepId);
                        if (handler is HumanApprovalStepHandler)
                        {
                            _logger.LogDebug(
                                "恢复步骤 {InstanceId}/{Step} 为 Recovering 状态（静默等待审批回调）",
                                instanceId,
                                stepId
                            );
                        }
                        else
                        {
                            _logger.LogDebug(
                                "恢复步骤 {InstanceId}/{Step} 为 Recovering 状态",
                                instanceId,
                                stepId
                            );
                            // AgentStep：启动超时 Timer 重发 webhook
                            StartRecoveryTimer(instanceId, stepId, record.InputSnapshot);
                        }
                    }
                }

                _instances[instanceId] = instance;
                _logger.LogInformation(
                    "恢复实例: {InstanceId}, 状态: {Status}, InFlight: {InFlight}",
                    instanceId,
                    instance.Status,
                    instance.InFlightStepIds.Count
                );
            }

            _logger.LogInformation("重启恢复完成: 已加载 {Count} 个实例", _instances.Count);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    /// <summary>
    /// 心跳超时标记实例为 timed-out（由 WorkflowHeartbeatService 委托调用）。
    /// 在分布式锁保护下执行：重新加载检查点 → 二次确认超时 → 更新内存和持久化。
    /// 超时实例标记为 timed-out 中间态（非 failed），允许后续恢复。
    /// </summary>
    public async Task MarkInstanceTimedOutAsync(
        string instanceId, TimeSpan effectiveThreshold, CancellationToken ct)
    {
        // 获取分布式锁，与回调/恢复重发序列化
        await using var distributedLock = await TryAcquireDistributedLockAsync(instanceId, ct);

        // 重新加载检查点以防 TOCTOU
        var checkpoint = await _stateStore.LoadAsync(instanceId, ct);
        if (checkpoint == null) return;

        // 二次确认：可能回调正好在获取锁期间到达
        var timeSinceHeartbeat = DateTime.UtcNow - checkpoint.LastHeartbeat;
        if (timeSinceHeartbeat <= effectiveThreshold)
            return;

        _logger.LogWarning("心跳超时标记实例: {InstanceId}, 最后心跳: {LastHeartbeat}, 超时: {Elapsed}",
            instanceId, checkpoint.LastHeartbeat, timeSinceHeartbeat);

        checkpoint.Status = "timed-out";

        // 标记所有活跃步骤为 failed（保留步骤级超时标记）
        foreach (var record in checkpoint.StepRecords)
        {
            if (record.Status is StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running)
            {
                record.Status = StepStatus.Failed;
                record.ErrorMessage = "心跳超时";
                record.CompletedAt = DateTime.UtcNow;
            }
        }

        await _stateStore.SaveAsync(checkpoint, ct);

        // 同步内存状态
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            instance.Status = "timed-out";

            foreach (var stepId in instance.InFlightStepIds)
            {
                var record = instance.StepRecords.Find(r => r.StepId == stepId);
                if (record is { Status: StepStatus.Dispatched or StepStatus.Recovering or StepStatus.Running })
                {
                    record.Status = StepStatus.Failed;
                    record.ErrorMessage = "心跳超时";
                    record.CompletedAt = DateTime.UtcNow;
                }

                // 取消对应的恢复 Timer
                var timerKey = $"{instanceId}:{stepId}";
                if (_recoveryTimers.TryRemove(timerKey, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                _recoveryTasks.TryRemove(timerKey, out var _);
            }

            instance.InFlightStepIds.Clear();
        }
    }

    /// <summary>
    /// 恢复超时实例 — 将 timed-out 实例恢复为 running，重新 Dispatch 因超时失败的步骤。
    /// 在分布式锁保护下执行，二次确认状态为 timed-out。
    /// </summary>
    public async Task ResumeTimedOutWorkflowAsync(string instanceId, CancellationToken ct = default)
    {
        // 获取分布式锁
        await using var distributedLock = await TryAcquireDistributedLockAsync(instanceId, ct);

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"实例不存在: {instanceId}");

        // 二次确认状态为 timed-out
        if (instance.Status != "timed-out")
            throw new InvalidOperationException($"实例状态不是 timed-out: {instance.Status}");

        _logger.LogInformation("恢复超时实例: {InstanceId}", instanceId);

        // 恢复实例状态
        instance.Status = "running";
        instance.LastHeartbeat = DateTime.UtcNow;
        instance.Context.IsRunning = true;

        // 恢复因超时失败的步骤
        var stepsToRedispatch = new List<string>();
        foreach (var record in instance.StepRecords)
        {
            if (record.Status == StepStatus.Failed && record.ErrorMessage == "心跳超时")
            {
                record.Status = StepStatus.Dispatched;
                record.ErrorMessage = null;
                record.CompletedAt = null;
                stepsToRedispatch.Add(record.StepId);
                instance.InFlightStepIds.Add(record.StepId);
                lock (instance.Context.SyncLock)
                    instance.Context.PendingStepIds.Add(record.StepId);
            }
        }

        await SaveCheckpointAsync(instance, ct);

        // 重新 Dispatch 恢复的步骤
        foreach (var stepId in stepsToRedispatch)
        {
            _ = ExecuteStepAsync(instance, stepId, triggeredBy: "resume", ct);
        }

        _logger.LogInformation(
            "超时实例已恢复: {InstanceId}, 重新 Dispatch {Count} 个步骤",
            instanceId, stepsToRedispatch.Count);
    }

    /// <summary>
    /// 获取子工作流映射关系。
    /// </summary>
    /// <param name="parentInstanceId">父工作流实例ID</param>
    /// <param name="stepId">子工作流步骤ID</param>
    /// <returns>子工作流实例ID,如果不存在则返回null</returns>
    public string? GetSubWorkflowInstanceId(string parentInstanceId, string stepId)
    {
        var key = $"{parentInstanceId}:{stepId}";
        return _subWorkflowMappings.TryGetValue(key, out var subInstanceId) ? subInstanceId : null;
    }

    /// <summary>
    /// 获取所有子工作流映射。
    /// </summary>
    /// <param name="parentInstanceId">父工作流实例ID</param>
    /// <returns>子工作流步骤ID到实例ID的映射字典</returns>
    public Dictionary<string, string> GetAllSubWorkflows(string parentInstanceId)
    {
        return _subWorkflowMappings
            .Where(kvp => kvp.Key.StartsWith($"{parentInstanceId}:"))
            .ToDictionary(
                kvp => kvp.Key.Substring(parentInstanceId.Length + 1),
                kvp => kvp.Value
            );
    }

    /// <summary>
    /// 关闭引擎，取消所有恢复 Timer 和 shutdown 令牌。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();

        // 清理子工作流映射
        _subWorkflowMappings.Clear();

        // 取消所有恢复 Timer
        foreach (var (_, cts) in _recoveryTimers)
            cts.Cancel();

        // 等待所有恢复任务完成，确保无人再访问 _shutdownCts.Token
        if (!_recoveryTasks.IsEmpty)
        {
            var tasks = _recoveryTasks.Values.ToArray();
            try { await Task.WhenAll(tasks); }
            catch { /* 忽略任务内部异常，已分别捕获 */ }
        }

        // 清理恢复 Timer 资源
        foreach (var (_, cts) in _recoveryTimers)
            cts.Dispose();
        _recoveryTimers.Clear();
        _recoveryTasks.Clear();

        _shutdownCts.Dispose();
    }

    /// <summary>
    /// 处理 Webhook 回调 — Hermes Agent 处理完成后回调 .NET 应用。
    /// </summary>
    public async Task OnWebhookCallbackAsync(
        string instanceId,
        string completedStepId,
        string? output,
        string? error,
        CancellationToken ct = default
    )
    {
        // 分布式锁保护（多实例部署场景）
        await using var distributedLock = await TryAcquireDistributedLockAsync(instanceId, ct);

        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            _logger.LogWarning("收到未知实例回调: {InstanceId}", instanceId);
            return;
        }

        // 自动恢复：若实例为 timed-out，先恢复为 running
        if (instance.Status == "timed-out")
        {
            _logger.LogInformation("回调自动恢复超时实例: {InstanceId}", instanceId);
            instance.Status = "running";
            instance.LastHeartbeat = DateTime.UtcNow;
            instance.Context.IsRunning = true;

            // 恢复因超时失败的步骤为 Recovering
            foreach (var timedOutRec in instance.StepRecords)
            {
                if (timedOutRec.Status == StepStatus.Failed && timedOutRec.ErrorMessage == "心跳超时")
                {
                    timedOutRec.Status = StepStatus.Recovering;
                    timedOutRec.ErrorMessage = null;
                    timedOutRec.CompletedAt = null;
                    instance.InFlightStepIds.Add(timedOutRec.StepId);
                    lock (instance.Context.SyncLock)
                        instance.Context.PendingStepIds.Add(timedOutRec.StepId);
                }
            }
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
                    instanceId,
                    completedStepId,
                    record.Status
                );
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

        // 取消恢复 Timer
        CancelRecoveryTimer(instanceId, completedStepId);

        // 更新追踪记录
        if (!string.IsNullOrEmpty(error))
        {
            MarkFailed(record, error, detail: null);
            ctx.IsRunning = false;
            CleanupInstanceResources(instanceId);
            _logger.LogError(
                "步骤失败: {InstanceId}/{Step}, 错误: {Error}",
                instanceId,
                completedStepId,
                error
            );
        }
        else
        {
            MarkCompleted(record);
            _logger.LogInformation(
                "步骤回调: {InstanceId}/{Step}, 耗时: {Duration}ms, 待完成: {Pending}",
                instanceId,
                completedStepId,
                record.Duration?.TotalMilliseconds ?? 0,
                ctx.PendingStepIds.Count
            );
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
            _logger.LogWarning(
                "工作流终止: {InstanceId}, 原因: {Reason}",
                instanceId,
                result.Error?.Message ?? "IsRunning=false"
            );
            instance.Status = "failed";

            CleanupInstanceResources(instanceId);
            await SaveCheckpointAsync(instance, ct);
            return;
        }

        // 推进下一步
        await AdvanceAsync(instance, result, completedStepId, ct);
    }

    /// <summary>获取工作流实例</summary>
    public WorkflowInstance? GetInstance(string instanceId) =>
        _instances.TryGetValue(instanceId, out var inst) ? inst : null;

    /// <summary>
    /// 计算实例的有效心跳阈值 — 考虑 InFlight 步骤的 HeartbeatExtension。
    /// effectiveThreshold = max(全局阈值, max(InFlight步骤的HeartbeatExtension ?? TimeSpan.Zero))
    /// </summary>
    internal TimeSpan GetEffectiveHeartbeatThreshold(string instanceId, TimeSpan globalThreshold)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return globalThreshold;

        var maxExtension = TimeSpan.Zero;
        foreach (var stepId in instance.InFlightStepIds)
        {
            if (!_handlers.TryGetValue(stepId, out var handler)
                || handler is not StepHandlerBase baseHandler)
                continue;

            // StepDefinition 的 heartbeat_extension 覆盖 Handler 虚属性
            var stepDef = GetStepDefinition(instance.WorkflowName, stepId);
            TimeSpan? extension;
            if (!string.IsNullOrWhiteSpace(stepDef?.HeartbeatExtension)
                && TimeSpan.TryParse(stepDef.HeartbeatExtension, out var parsed))
            {
                extension = parsed;
            }
            else
            {
                extension = baseHandler.HeartbeatExtension;
            }

            if (extension.HasValue && extension.Value > maxExtension)
                maxExtension = extension.Value;
        }

        return maxExtension > globalThreshold ? maxExtension : globalThreshold;
    }

    /// <summary>获取实例的所有步骤记录（按 StartedAt 排序）</summary>
    public IReadOnlyList<StepRecord> GetStepRecords(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return Array.Empty<StepRecord>();

        return instance
            .StepRecords.OrderBy(r => r.StartedAt == default ? DateTime.MaxValue : r.StartedAt)
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
        if (records.Count == 0)
            return "(无记录)";

        var sb = new StringBuilder();
        sb.AppendLine($"工作流实例: {instanceId}");
        sb.AppendLine(new string('-', 80));

        foreach (var r in records)
        {
            var time =
                r.StartedAt == default ? "--------.---" : r.StartedAt.ToString("HH:mm:ss.fff");
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
        WorkflowInstance instance,
        string stepId,
        string? triggeredBy,
        CancellationToken ct
    )
    {
        var ctx = instance.Context;

        if (!_handlers.TryGetValue(stepId, out var handler))
        {
            _logger.LogError("未注册的步骤: {Step}", stepId);
            ctx.IsRunning = false;
            instance.Status = "failed";
            CleanupInstanceResources(instance.Context.InstanceId);
            return;
        }

        var record = GetOrCreateRecord(instance, stepId);
        record.TriggeredBy = triggeredBy;

        // 通过 workflowName + stepId 复合键查询 StepDefinition
        var stepDef = GetStepDefinition(instance.WorkflowName, stepId);

        if (handler is SubWorkflowStepHandler subWorkflowHandler)
        {
            await ExecuteSubWorkflowStepAsync(instance, stepId, subWorkflowHandler, record, stepDef, ct);
        }
        else if (handler is AgentStepHandler agentHandler)
        {
            await ExecuteAgentStepAsync(instance, stepId, agentHandler, record, stepDef, ct);
        }
        else if (handler is HumanApprovalStepHandler approvalHandler)
        {
            await ExecuteHumanApprovalStepAsync(instance, stepId, approvalHandler, record, stepDef, ct);
        }
        else if (handler is CodeStepHandler codeHandler)
        {
            await ExecuteCodeStepAsync(instance, stepId, codeHandler, record, stepDef, ct);
        }
        else if (handler is DelayStepHandler delayHandler)
        {
            await ExecuteDelayStepAsync(instance, stepId, delayHandler, record, stepDef, ct);
        }
        else
        {
            _logger.LogError("不支持的步骤处理器类型: {Type}", handler.GetType().Name);
            record.Status = StepStatus.Failed;
            record.ErrorMessage = $"不支持的类型: {handler.GetType().Name}";
            ctx.IsRunning = false;
            instance.Status = "failed";
            CleanupInstanceResources(instance.Context.InstanceId);
            await SaveCheckpointAsync(instance, ct);
        }
    }

    private async Task ExecuteAgentStepAsync(
        WorkflowInstance instance,
        string stepId,
        AgentStepHandler agentHandler,
        StepRecord record,
        StepDefinition? stepDef,
        CancellationToken ct
    )
    {
        var ctx = instance.Context;
        var mergedConfig = MergeStepRuntimeConfig(agentHandler, stepDef);

        // 解析通信模式：YAML communication_mode > Handler.Mode
        var effectiveMode = ResolveAgentMode(agentHandler, stepDef);

        // 创建超时监控（如果 StepDefinition 配置了 timeout）
        var timeoutConfig = YamlConfigConverter.ConvertTimeoutConfig(mergedConfig.Timeout, mergedConfig.TimeoutAction);
        using var timeoutMonitor = timeoutConfig != null
            ? new TimeoutMonitor(stepId, ctx.InstanceId, timeoutConfig, LoggerFactory.Create(b => { }).CreateLogger<TimeoutMonitor>())
            : null;

        if (effectiveMode == AgentCommunicationMode.RunClient)
        {
            // RunClient SSE 模式
            record.Status = StepStatus.Running;
            record.StartedAt = DateTime.UtcNow;

            var prompt = ResolvePromptTemplate(mergedConfig.Prompt, ctx) ?? agentHandler.BuildPrompt(ctx);
            record.InputSnapshot = prompt;

            // 构建步骤执行的取消令牌（关联超时和 shutdown）
            using var linkedCts = timeoutMonitor != null
                ? CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, timeoutMonitor.CancellationToken)
                : null;
            var stepCt = linkedCts?.Token ?? _shutdownCts.Token;

            // 判断是否应用重试
            var shouldRetry = mergedConfig.Retry != null && effectiveMode == AgentCommunicationMode.RunClient;

            try
            {
                if (shouldRetry)
                {
                    // 使用重试执行器包装 RunClient 调用
                    var retryConfig = YamlConfigConverter.ConvertRetryConfig(mergedConfig.Retry!);
                    await _retryExecutor.ExecuteWithRetryAsync<string>(async innerCt =>
                    {
                        var runId = await _runClient.StartAsync(prompt, agentHandler.RunOptions, innerCt);
                        _logger.LogDebug("RunClient 已启动: {InstanceId}/{Step}, RunId: {RunId}", ctx.InstanceId, stepId, runId);

                        await foreach (var evt in _runClient.SubscribeEventsAsync(runId, innerCt))
                        {
                            if (evt is { Type: "run.completed" })
                            {
                                lock (ctx.SyncLock)
                                    ctx.StepOutputs[stepId] = evt.OutPut;
                                record.OutputSnapshot = evt.OutPut?.ToString();
                                break;
                            }
                        }
                        return runId;
                    }, retryConfig, stepId, ctx.InstanceId, stepCt);
                }
                else
                {
                    var runId = await _runClient.StartAsync(prompt, agentHandler.RunOptions, stepCt);
                    _logger.LogDebug("RunClient 已启动: {InstanceId}/{Step}, RunId: {RunId}", ctx.InstanceId, stepId, runId);

                    await foreach (var evt in _runClient.SubscribeEventsAsync(runId, stepCt))
                    {
                        if (evt is { Type: "run.completed" })
                        {
                            lock (ctx.SyncLock)
                                ctx.StepOutputs[stepId] = evt.OutPut;
                            record.OutputSnapshot = evt.OutPut?.ToString();
                            break;
                        }
                    }
                }

                MarkCompleted(record);
                await SaveCheckpointAsync(instance, ct);

                // 检查 ParallelJoin：若为并行子步骤，递减计数器而非自行推进
                if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                    return;

                // 推进下一步
                var result = await agentHandler.ExecuteAsync(ctx, ct);
                record.TriggeredSteps = result.NextStepIds?.ToList();
                await AdvanceAsync(instance, result, stepId, ct);
            }
            catch (Exception ex) when (timeoutMonitor != null && timeoutMonitor.CancellationToken.IsCancellationRequested && !_shutdownCts.IsCancellationRequested)
            {
                // 超时异常优先处理
                MarkFailed(record, $"步骤超时: {ex.Message}", ex.ToString());
                record.FullStackTrace = ex.ToString();

                var errorPolicy = YamlConfigConverter.ConvertErrorPolicy(mergedConfig.ErrorPolicy);
                if (errorPolicy != null)
                {
                    await _errorHandler.HandleErrorAsync(instance, record, ex, errorPolicy.Value, ct);

                    if (errorPolicy.Value == ErrorPolicy.SkipFailedBranch)
                    {
                        // SkipFailedBranch：标记失败但不终止工作流，让 join 计数器自然递减
                        if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                            return;
                        // 非并行上下文：降级为 FailFast
                        _logger.LogWarning("步骤 {StepId} 配置了 SkipFailedBranch 但不在并行分支中，降级处理", stepId);
                        ctx.IsRunning = false;
                        CleanupInstanceResources(instance.Context.InstanceId);
                        await SaveCheckpointAsync(instance, ct);
                        return;
                    }
                }
                else
                {
                    ctx.IsRunning = false;
                    CleanupInstanceResources(instance.Context.InstanceId);
                    await SaveCheckpointAsync(instance, ct);
                }
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Engine shutdown 触发的取消 — 不重试，静默退出
                _logger.LogInformation("步骤 {StepId} 因 Engine 关闭而取消", stepId);
                record.Status = StepStatus.Pending; // 重启后可恢复
                await SaveCheckpointAsync(instance, ct);
            }
            catch (Exception ex)
            {
                MarkFailed(record, ex.Message, ex.ToString());
                record.FullStackTrace = ex.ToString();

                var errorPolicy = YamlConfigConverter.ConvertErrorPolicy(mergedConfig.ErrorPolicy);
                if (errorPolicy != null)
                {
                    await _errorHandler.HandleErrorAsync(instance, record, ex, errorPolicy.Value, ct);

                    if (errorPolicy.Value == ErrorPolicy.SkipFailedBranch)
                    {
                        // SkipFailedBranch：标记失败但不终止工作流，让 join 计数器自然递减
                        if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                            return;
                        // 非并行上下文：降级为 FailFast
                        _logger.LogWarning("步骤 {StepId} 配置了 SkipFailedBranch 但不在并行分支中，降级处理", stepId);
                        ctx.IsRunning = false;
                        CleanupInstanceResources(instance.Context.InstanceId);
                        await SaveCheckpointAsync(instance, ct);
                        return;
                    }
                }
                else
                {
                    ctx.IsRunning = false;
                    CleanupInstanceResources(instance.Context.InstanceId);
                    await SaveCheckpointAsync(instance, ct);
                }
            }
        }
        else if (effectiveMode == AgentCommunicationMode.Webhook)
        {
            // Webhook 模式 — 发送 webhook 回调
            record.Status = StepStatus.Dispatched;
            record.StartedAt = DateTime.Now;

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
                    mergedConfig.RouteName ?? agentHandler.RouteName,
                    mergedConfig.EventType ?? agentHandler.EventType,
                    payload,
                    ct: ct
                );

                _logger.LogDebug(
                    "Webhook 已发送: {InstanceId}/{Step}, 状态: {Status}",
                    ctx.InstanceId,
                    stepId,
                    webhookResult.Status
                );
            }
            catch (Exception ex)
            {
                MarkFailed(record, ex.Message, ex.ToString());
                lock (instance.SyncLock)
                    instance.InFlightStepIds.Remove(stepId);
                record.FullStackTrace = ex.ToString();
                _logger.LogError(ex, "Webhook 发送失败: {InstanceId}/{Step}", ctx.InstanceId, stepId);
                ctx.IsRunning = false;
                CleanupInstanceResources(instance.Context.InstanceId);
                await SaveCheckpointAsync(instance, ct);
            }
        }
        else
        {
            // None 模式 — 不调用 Hermes Agent，本地直接完成
            record.Status = StepStatus.Running;
            record.StartedAt = DateTime.UtcNow;

            var prompt = ResolvePromptTemplate(mergedConfig.Prompt, ctx) ?? agentHandler.BuildPrompt(ctx);
            record.InputSnapshot = prompt;

            try
            {
                MarkCompleted(record);
                await SaveCheckpointAsync(instance, ct);

                if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                    return;

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
    }

    private async Task ExecuteCodeStepAsync(
        WorkflowInstance instance,
        string stepId,
        CodeStepHandler codeHandler,
        StepRecord record,
        StepDefinition? stepDef,
        CancellationToken ct
    )
    {
        var ctx = instance.Context;
        var mergedConfig = MergeStepRuntimeConfig(codeHandler, stepDef);

        // 创建超时监控
        var timeoutConfig = YamlConfigConverter.ConvertTimeoutConfig(mergedConfig.Timeout, mergedConfig.TimeoutAction);
        using var timeoutMonitor = timeoutConfig != null
            ? new TimeoutMonitor(stepId, ctx.InstanceId, timeoutConfig, LoggerFactory.Create(b => { }).CreateLogger<TimeoutMonitor>())
            : null;

        record.Status = StepStatus.Running;
        record.StartedAt = DateTime.UtcNow;

        record.InputSnapshot = JsonSerializer.Serialize(
            new
            {
                ctx.InstanceId,
                StepOutputs = ctx.StepOutputs.Keys.ToList(),
                Data = ctx.Data.Keys.ToList(),
                stepId,
            }
        );

        await SaveCheckpointAsync(instance, ct);

        // 构建超时关联的取消令牌
        using var linkedCts = timeoutMonitor != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, timeoutMonitor.CancellationToken)
            : null;
        var stepCt = linkedCts?.Token ?? _shutdownCts.Token;

        var shouldRetry = mergedConfig.Retry != null;
        StepResult result;

        try
        {
            if (shouldRetry)
            {
                var retryConfig = YamlConfigConverter.ConvertRetryConfig(mergedConfig.Retry!);
                result = await _retryExecutor.ExecuteWithRetryAsync<StepResult>(async innerCt =>
                {
                    var r = await codeHandler.ExecuteAsync(ctx, innerCt);
                    if (!r.IsSuccess)
                        throw new StepRetryException(r.Error?.Message ?? "步骤失败", r.Error);
                    return r;
                }, retryConfig, stepId, ctx.InstanceId, stepCt);
            }
            else
            {
                result = await codeHandler.ExecuteAsync(ctx, stepCt);
            }
        }
        catch (StepRetryException retryEx)
        {
            // 重试耗尽仍失败 — 转为 StepResult 交给后续 error_policy 处理
            result = new StepResult { IsSuccess = false, Error = retryEx.InnerException ?? retryEx };
        }
        catch (Exception ex) when (timeoutMonitor != null && timeoutMonitor.CancellationToken.IsCancellationRequested && !_shutdownCts.IsCancellationRequested)
        {
            // 超时异常优先处理
            MarkFailed(record, $"步骤超时: {ex.Message}", ex.ToString());
            record.FullStackTrace = ex.ToString();

            var errorPolicy = YamlConfigConverter.ConvertErrorPolicy(mergedConfig.ErrorPolicy);
            if (errorPolicy != null)
            {
                await _errorHandler.HandleErrorAsync(instance, record, ex, errorPolicy.Value, ct);

                if (errorPolicy.Value == ErrorPolicy.SkipFailedBranch)
                {
                    // SkipFailedBranch：标记失败但不终止工作流，让 join 计数器自然递减
                    if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                        return;
                    // 非并行上下文：降级为 FailFast
                    _logger.LogWarning("步骤 {StepId} 配置了 SkipFailedBranch 但不在并行分支中，降级处理", stepId);
                    ctx.IsRunning = false;
                    instance.Status = "failed";
                    CleanupInstanceResources(instance.Context.InstanceId);
                    await SaveCheckpointAsync(instance, ct);
                    return;
                }
                // FailFast / ContinueOnError：HandleErrorAsync 已完成处理
            }
            else
            {
                ctx.IsRunning = false;
                instance.Status = "failed";
                CleanupInstanceResources(instance.Context.InstanceId);
                await SaveCheckpointAsync(instance, ct);
            }
            return;
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            _logger.LogInformation("步骤 {StepId} 因 Engine 关闭而取消", stepId);
            record.Status = StepStatus.Pending;
            await SaveCheckpointAsync(instance, ct);
            return;
        }
        catch (Exception ex)
        {
            MarkFailed(record, ex.Message, ex.ToString());
            record.FullStackTrace = ex.ToString();

            var errorPolicy = YamlConfigConverter.ConvertErrorPolicy(mergedConfig.ErrorPolicy);
            if (errorPolicy != null)
            {
                await _errorHandler.HandleErrorAsync(instance, record, ex, errorPolicy.Value, ct);

                if (errorPolicy.Value == ErrorPolicy.SkipFailedBranch)
                {
                    // SkipFailedBranch：标记失败但不终止工作流，让 join 计数器自然递减
                    if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                        return;
                    // 非并行上下文：降级为 FailFast
                    _logger.LogWarning("步骤 {StepId} 配置了 SkipFailedBranch 但不在并行分支中，降级处理", stepId);
                    ctx.IsRunning = false;
                    instance.Status = "failed";
                    CleanupInstanceResources(instance.Context.InstanceId);
                    await SaveCheckpointAsync(instance, ct);
                    return;
                }
                // FailFast / ContinueOnError：HandleErrorAsync 已完成处理
            }
            else
            {
                ctx.IsRunning = false;
                instance.Status = "failed";
                CleanupInstanceResources(instance.Context.InstanceId);
                await SaveCheckpointAsync(instance, ct);
            }
            return;
        }

        lock (ctx.SyncLock)
            ctx.StepOutputs[stepId] = result.Output;
        record.OutputSnapshot = result.Output?.ToString();

        if (!result.IsSuccess || !ctx.IsRunning)
        {
            MarkFailed(
                record,
                result.Error?.Message ?? "IsRunning=false",
                result.Error?.ToString()
            );
            record.FullStackTrace = result.Error?.ToString();

            var errorPolicy = YamlConfigConverter.ConvertErrorPolicy(mergedConfig.ErrorPolicy);
            if (errorPolicy != null && result.Error != null)
            {
                await _errorHandler.HandleErrorAsync(instance, record, result.Error, errorPolicy.Value, ct);

                if (errorPolicy.Value == ErrorPolicy.SkipFailedBranch)
                {
                    // SkipFailedBranch：标记失败但不终止工作流，让 join 计数器自然递减
                    if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
                        return;
                    // 非并行上下文：降级为 FailFast
                    _logger.LogWarning("步骤 {StepId} 配置了 SkipFailedBranch 但不在并行分支中，降级处理", stepId);
                    ctx.IsRunning = false;
                    instance.Status = "failed";
                    CleanupInstanceResources(instance.Context.InstanceId);
                    await SaveCheckpointAsync(instance, ct);
                    return;
                }
                // FailFast / ContinueOnError：HandleErrorAsync 已完成处理
                return;
            }

            ctx.IsRunning = false;
            instance.Status = "failed";
            CleanupInstanceResources(instance.Context.InstanceId);
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
            // 自动路由：handler 未指定下一步时，从 YAML 元数据推导
            var autoNextIds = ResolveAutoNextSteps(instance, stepId);
            if (autoNextIds is { Count: > 0 })
            {
                result.NextStepIds = autoNextIds;
                record.TriggeredSteps = new List<string>(autoNextIds);
                foreach (var nextId in autoNextIds)
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
    }

    /// <summary>
    /// 从 YAML 步骤定义推导自动下一步：优先 next_step_id，其次 depends_on 反向匹配。
    /// </summary>
    private List<string>? ResolveAutoNextSteps(WorkflowInstance instance, string completedStepId)
    {
        var stepDef = GetStepDefinition(instance.WorkflowName, completedStepId);
        if (stepDef == null) return null;

        // 优先使用 step 自身声明的 next_step_id
        if (!string.IsNullOrEmpty(stepDef.NextStepId))
            return [stepDef.NextStepId];

        // 其次扫描同工作流中所有 depends_on 指向本步骤的 step
        var prefix = (instance.WorkflowName ?? "__default__") + ":";
        var dependentIds = _stepDefinitions
            .Where(kvp => kvp.Key.StartsWith(prefix))
            .Select(kvp => kvp.Value)
            .Where(def => def.DependsOn != null && def.DependsOn.Contains(completedStepId))
            .Select(def => def.Id)
            .ToList();

        return dependentIds.Count > 0 ? dependentIds : null;
    }

    private async Task ExecuteHumanApprovalStepAsync(
        WorkflowInstance instance,
        string stepId,
        HumanApprovalStepHandler approvalHandler,
        StepRecord record,
        StepDefinition? stepDef,
        CancellationToken ct
    )
    {
        var ctx = instance.Context;
        var mergedConfig = MergeStepRuntimeConfig(approvalHandler, stepDef);

        // 创建超时监控
        var timeoutConfig = YamlConfigConverter.ConvertTimeoutConfig(mergedConfig.Timeout, mergedConfig.TimeoutAction);
        using var timeoutMonitor = timeoutConfig != null
            ? new TimeoutMonitor(stepId, ctx.InstanceId, timeoutConfig, LoggerFactory.Create(b => { }).CreateLogger<TimeoutMonitor>())
            : null;

        record.Status = StepStatus.Dispatched;
        record.StartedAt = DateTime.UtcNow;
        record.InputSnapshot = approvalHandler.BuildApprovalMessage(ctx);

        lock (ctx.SyncLock)
            ctx.PendingStepIds.Add(stepId);
        lock (instance.SyncLock)
            instance.InFlightStepIds.Add(stepId);

        await SaveCheckpointAsync(instance, ct);

        try
        {
            // Handler 自行发送审批通知，无返回值，步骤保持 Dispatched
            await approvalHandler.DispatchAsync(ctx, ct);
            _logger.LogInformation(
                "人工审批已分发: {InstanceId}/{Step}, 等待审批回调",
                ctx.InstanceId,
                stepId
            );
        }
        catch (Exception ex)
        {
            MarkFailed(record, ex.Message, ex.ToString());
            record.FullStackTrace = ex.ToString();
            lock (instance.SyncLock)
                instance.InFlightStepIds.Remove(stepId);
            lock (ctx.SyncLock)
                ctx.PendingStepIds.Remove(stepId);
            ctx.IsRunning = false;
            _logger.LogError(ex, "审批分发失败: {InstanceId}/{Step}", ctx.InstanceId, stepId);
            CleanupInstanceResources(instance.Context.InstanceId);
            await SaveCheckpointAsync(instance, ct);
        }
    }

    /// <summary>
    /// 处理人工审批回调 — 审批人做出决策后回调。
    /// 审批拒绝是正常业务路径（MarkCompleted），不是错误（MarkFailed）。
    /// </summary>
    public async Task OnHumanApprovalCallbackAsync(
        string instanceId,
        string stepId,
        string decision,
        string? comment,
        string? approverId,
        CancellationToken ct = default
    )
    {
        // 分布式锁保护
        await using var distributedLock = await TryAcquireDistributedLockAsync(instanceId, ct);

        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            _logger.LogWarning("收到未知实例审批回调: {InstanceId}", instanceId);
            return;
        }

        // 自动恢复：若实例为 timed-out，先恢复为 running
        if (instance.Status == "timed-out")
        {
            _logger.LogInformation("审批回调自动恢复超时实例: {InstanceId}", instanceId);
            instance.Status = "running";
            instance.LastHeartbeat = DateTime.UtcNow;
            instance.Context.IsRunning = true;

            // 恢复因超时失败的步骤为 Recovering
            foreach (var timedOutRec in instance.StepRecords)
            {
                if (timedOutRec.Status == StepStatus.Failed && timedOutRec.ErrorMessage == "心跳超时")
                {
                    timedOutRec.Status = StepStatus.Recovering;
                    timedOutRec.ErrorMessage = null;
                    timedOutRec.CompletedAt = null;
                    instance.InFlightStepIds.Add(timedOutRec.StepId);
                    lock (instance.Context.SyncLock)
                        instance.Context.PendingStepIds.Add(timedOutRec.StepId);
                }
            }
        }

        var ctx = instance.Context;

        // 幂等防护
        StepRecord record;
        lock (instance.SyncLock)
        {
            record = GetOrCreateRecord(instance, stepId);
            if (record.Status is StepStatus.Completed or StepStatus.Failed)
            {
                _logger.LogWarning(
                    "幂等防护: {InstanceId}/{StepId} 已被处理 (Status={Status}), 忽略重复审批回调",
                    instanceId,
                    stepId,
                    record.Status
                );
                return;
            }
        }

        // 封装审批结果
        var approvalResult = new ApprovalResult
        {
            Decision = decision,
            Comment = comment,
            ApproverId = approverId,
        };

        // 记录输出
        lock (ctx.SyncLock)
        {
            ctx.StepOutputs[stepId] = approvalResult;
            ctx.PendingStepIds.Remove(stepId);
        }
        lock (instance.SyncLock)
        {
            instance.InFlightStepIds.Remove(stepId);
        }
        record.OutputSnapshot = $"Decision={decision}, Comment={comment}, Approver={approverId}";

        // 取消恢复 Timer
        CancelRecoveryTimer(instanceId, stepId);

        // 审批结果统一走 MarkCompleted（拒绝也是正常业务路径）
        MarkCompleted(record);
        _logger.LogInformation(
            "审批回调: {InstanceId}/{Step}, 决策: {Decision}, 审批人: {Approver}",
            instanceId,
            stepId,
            decision,
            approverId
        );

        // 持久化
        await SaveCheckpointAsync(instance, ct);

        // 检查 ParallelJoin
        if (await TryDecrementJoinAndMaybeAdvanceAsync(instance, stepId, ct))
            return;

        // 并行步骤检查
        var pendingCount = 0;
        lock (ctx.SyncLock)
            pendingCount = ctx.PendingStepIds.Count;
        if (pendingCount > 0)
        {
            _logger.LogDebug("并行步骤尚未全部完成，继续等待: {Remaining}", pendingCount);
            return;
        }

        // 工作流已终止
        if (!ctx.IsRunning)
        {
            _logger.LogWarning("工作流已终止: {InstanceId}，不再推进", instanceId);
            return;
        }

        // 找到 Handler 决定分支路由
        if (!_handlers.TryGetValue(stepId, out var handler))
        {
            _logger.LogError("未找到步骤处理器: {Step}", stepId);
            return;
        }

        var result = await handler.ExecuteAsync(ctx, ct);

        if (record != null && result.NextStepIds is { Count: > 0 })
        {
            record.TriggeredSteps = new List<string>(result.NextStepIds);
        }

        if (!result.IsSuccess || !ctx.IsRunning)
        {
            _logger.LogWarning(
                "工作流终止: {InstanceId}, 原因: {Reason}",
                instanceId,
                result.Error?.Message ?? "IsRunning=false"
            );
            instance.Status = "failed";
            await SaveCheckpointAsync(instance, ct);
            CleanupInstanceResources(instanceId);
            return;
        }

        await AdvanceAsync(instance, result, stepId, ct);
    }

    private async Task ExecuteSubWorkflowStepAsync(
        WorkflowInstance parentInstance,
        string stepId,
        SubWorkflowStepHandler subWorkflowHandler,
        StepRecord record,
        StepDefinition? stepDef,
        CancellationToken ct
    )
    {
        var parentCtx = parentInstance.Context;
        var mergedConfig = MergeStepRuntimeConfig(subWorkflowHandler, stepDef);

        // 创建超时监控（监控子工作流整体执行时间）
        var timeoutConfig = YamlConfigConverter.ConvertTimeoutConfig(mergedConfig.Timeout, mergedConfig.TimeoutAction);
        using var timeoutMonitor = timeoutConfig != null
            ? new TimeoutMonitor(stepId, parentCtx.InstanceId, timeoutConfig, LoggerFactory.Create(b => { }).CreateLogger<TimeoutMonitor>())
            : null;

        record.Status = StepStatus.Running;
        record.StartedAt = DateTime.UtcNow;

        // 构建子工作流输入
        var subWorkflowInput = subWorkflowHandler.BuildSubWorkflowInput(parentCtx);
        record.InputSnapshot = JsonSerializer.Serialize(new
        {
            subWorkflowName = subWorkflowHandler.SubWorkflowName,
            subWorkflowVersion = subWorkflowHandler.SubWorkflowVersion,
            input = subWorkflowInput
        });

        await SaveCheckpointAsync(parentInstance, ct);

        try
        {
            if (_workflowRegistry == null)
            {
                throw new InvalidOperationException(
                    "子工作流执行需要注册WorkflowRegistry。请在构造函数中提供WorkflowRegistry参数。");
            }

            // 获取子工作流定义
            var workflowName = subWorkflowHandler.SubWorkflowName;
            var workflowVersion = subWorkflowHandler.SubWorkflowVersion;

            var definition = workflowVersion != null
                ? _workflowRegistry.GetByVersion(workflowName, workflowVersion)
                : _workflowRegistry.Get(workflowName);

            // 创建子工作流上下文
            var subWorkflowContext = new WorkflowContext
            {
                InstanceId = $"sub-{parentCtx.InstanceId}-{stepId}",
                InitialInput = subWorkflowInput,
                WorkflowName = workflowName,
                WorkflowId = definition.Id,
            };

            _logger.LogInformation(
                "启动子工作流: {ParentInstanceId}/{StepId} → {SubWorkflowInstanceId} ({WorkflowName}:{Version})",
                parentCtx.InstanceId,
                stepId,
                subWorkflowContext.InstanceId,
                workflowName,
                workflowVersion ?? "latest"
            );

            // 记录父子映射关系
            var mappingKey = $"{parentCtx.InstanceId}:{stepId}";
            _subWorkflowMappings[mappingKey] = subWorkflowContext.InstanceId;

            // 创建新的引擎实例来执行子工作流(共享相同的handlers和registry)
            var subWorkflowEngine = new WorkflowEngine(
                _handlers.Values,
                _webhookClient,
                _runClient,
                _logger,
                _stateStore,
                _workflowRegistry
            );

            // 异步启动子工作流并等待完成
            var subWorkflowTask = Task.Run(async () =>
            {
                try
                {
                    // 从定义中获取入口步骤(第一个步骤)
                    var entryStepId = definition.Steps.FirstOrDefault()?.Id;
                    if (entryStepId == null)
                        throw new InvalidOperationException($"工作流 {workflowName} 没有定义步骤");

                    await subWorkflowEngine.StartAsync(entryStepId, subWorkflowContext, ct, workflowName);

                    // 使用 500ms 间隔轮询等待子工作流完成（相比 100ms 减少 80% 的轮询次数）
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        
                        var subInstance = subWorkflowEngine.GetInstance(subWorkflowContext.InstanceId);
                        if (subInstance == null || !subInstance.Context.IsRunning)
                            return subInstance;

                        await Task.Delay(500, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "子工作流执行失败: {SubWorkflowInstanceId}", subWorkflowContext.InstanceId);
                    throw;
                }
                finally
                {
                    await subWorkflowEngine.DisposeAsync();
                }
            }, ct);

            var subInstance = await subWorkflowTask;

            if (subInstance == null || subInstance.Status != "completed")
            {
                throw new InvalidOperationException(
                    $"子工作流执行失败,状态: {subInstance?.Status ?? "unknown"}");
            }

            // 提取子工作流输出并映射回父上下文
            var subWorkflowOutput = new Dictionary<string, object?>();
            foreach (var kvp in subInstance.Context.StepOutputs)
            {
                subWorkflowOutput[kvp.Key] = kvp.Value;
            }

            subWorkflowHandler.MapSubWorkflowOutput(parentCtx, subWorkflowOutput);

            // 记录子工作流输出
            lock (parentCtx.SyncLock)
                parentCtx.StepOutputs[stepId] = subWorkflowOutput;

            record.OutputSnapshot = JsonSerializer.Serialize(subWorkflowOutput);
            MarkCompleted(record);

            _logger.LogInformation(
                "子工作流完成: {ParentInstanceId}/{StepId} ← {SubWorkflowInstanceId}",
                parentCtx.InstanceId,
                stepId,
                subWorkflowContext.InstanceId
            );

            // 检查 ParallelJoin
            if (await TryDecrementJoinAndMaybeAdvanceAsync(parentInstance, stepId, ct))
                return;

            // 推进到下一步
            var result = await subWorkflowHandler.ExecuteAsync(parentCtx, ct);
            record.TriggeredSteps = result.NextStepIds?.ToList();
            await AdvanceAsync(parentInstance, result, stepId, ct);
        }
        catch (Exception ex)
        {
            MarkFailed(record, ex.Message, ex.ToString());
            record.FullStackTrace = ex.ToString();
            parentCtx.IsRunning = false;
            CleanupInstanceResources(parentInstance.Context.InstanceId);
            await SaveCheckpointAsync(parentInstance, ct);
        }
    }

    private async Task ExecuteDelayStepAsync(
        WorkflowInstance instance,
        string stepId,
        DelayStepHandler delayHandler,
        StepRecord record,
        StepDefinition? stepDef,
        CancellationToken ct
    )
    {
        var ctx = instance.Context;
        var mergedConfig = MergeStepRuntimeConfig(delayHandler, stepDef);

        // Delay 步骤：delay 本身是一种 timeout，此处仅记录配置
        // 超时监控在 delay 本身完成前不触发，仅用于外部超时覆盖
        var timeoutConfig = YamlConfigConverter.ConvertTimeoutConfig(mergedConfig.Timeout, mergedConfig.TimeoutAction);
        using var timeoutMonitor = timeoutConfig != null
            ? new TimeoutMonitor(stepId, ctx.InstanceId, timeoutConfig, LoggerFactory.Create(b => { }).CreateLogger<TimeoutMonitor>())
            : null;

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
                CleanupInstanceResources(instance.Context.InstanceId);
            }
        }
        catch (Exception ex)
        {
            MarkFailed(record, ex.Message, ex.ToString());
            record.FullStackTrace = ex.ToString();
            ctx.IsRunning = false;
            await SaveCheckpointAsync(instance, ct);
            CleanupInstanceResources(instance.Context.InstanceId);
        }
    }

    /// <summary>
    /// 推进工作流 — 根据 StepResult 决定串行/并行/ParallelJoin。
    /// </summary>
    private async Task AdvanceAsync(
        WorkflowInstance instance,
        StepResult result,
        string completedStepId,
        CancellationToken ct
    )
    {
        if (result.NextStepIds is not { Count: > 0 })
        {
            instance.Status = "completed";
            instance.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("工作流完成: {InstanceId}", instance.Context.InstanceId);
            CleanupInstanceResources(instance.Context.InstanceId);
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
                WaitMode = result.WaitMode,
            };
            _joinTrackers[instance.Context.InstanceId] = tracker;

            _logger.LogDebug(
                "ParallelJoin 初始化: {InstanceId}, 下游={Downstream}, 子步骤={Children}",
                instance.Context.InstanceId,
                result.JoinDownstreamStepId,
                string.Join(", ", result.NextStepIds)
            );

            var tasks = result.NextStepIds.Select(
                id => ExecuteStepAsync(instance, id, triggeredBy: completedStepId, ct)
            );
            await Task.WhenAll(tasks);
        }
        // 并行模式：并发启动多个子步骤
        else if (result.WaitForParallelCompletion)
        {
            lock (instance.Context.SyncLock)
                foreach (var stepId in result.NextStepIds)
                    instance.Context.PendingStepIds.Add(stepId);

            // ParallelAny 模式：注册一个无 JoinDownstream 的 tracker 用于 Any 追踪
            if (result.WaitMode == ParallelWaitMode.Any)
            {
                var tracker = new JoinTracker
                {
                    JoinDownstreamStepId = "", // 无下游汇合
                    TotalCount = result.NextStepIds.Count,
                    RemainingCount = result.NextStepIds.Count,
                    WaitMode = ParallelWaitMode.Any,
                };
                _joinTrackers[instance.Context.InstanceId] = tracker;

                _logger.LogDebug(
                    "ParallelAny 初始化: {InstanceId}, 子步骤={Children}",
                    instance.Context.InstanceId,
                    string.Join(", ", result.NextStepIds)
                );
            }

            var tasks = result.NextStepIds.Select(
                id => ExecuteStepAsync(instance, id, triggeredBy: completedStepId, ct)
            );
            await Task.WhenAll(tasks);
        }
        // 串行模式：逐一推进
        else
        {
            if (result.NextStepIds.Count > 1)
            {
                _logger.LogWarning(
                    "串行模式下 NextStepIds 包含 {Count} 个步骤，仅推进第一个 {First}，其余被忽略: {All}",
                    result.NextStepIds.Count,
                    result.NextStepIds[0],
                    string.Join(", ", result.NextStepIds)
                );
            }
            await ExecuteStepAsync(
                instance,
                result.NextStepIds[0],
                triggeredBy: completedStepId,
                ct
            );
        }
    }

    // =================================================================
    // 持久化辅助
    // =================================================================

    private async Task SaveCheckpointAsync(
        WorkflowInstance instance,
        CancellationToken ct = default
    )
    {
        try
        {
            instance.LastHeartbeat = DateTime.UtcNow;
            var checkpoint = WorkflowCheckpoint.FromInstance(instance, new Dictionary<string, string>(_subWorkflowMappings));
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
            if (existing != null)
                return existing;

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

    /// <summary>启动时预建工作流步骤的 Pending 记录。返回实际步骤数。</summary>
    private int InitializeRecords(WorkflowInstance instance, string? workflowName)
    {
        // 获取该工作流中定义的步骤 ID 集合
        var workflowStepIds = new HashSet<string>();
        if (workflowName != null)
        {
            var prefix = workflowName + ":";
            foreach (var kvp in _stepDefinitions)
            {
                if (kvp.Key.StartsWith(prefix))
                    workflowStepIds.Add(kvp.Value.Id);
            }
        }

        // 如果无定义，回退到所有注册 handler（向后兼容）
        if (workflowStepIds.Count == 0)
        {
            foreach (var (stepId, _) in _handlers)
                workflowStepIds.Add(stepId);
        }

        foreach (var stepId in workflowStepIds)
        {
            var stepType = _handlers.TryGetValue(stepId, out _) ? GetStepType(stepId) : "???";
            instance.StepRecords.Add(
                new StepRecord
                {
                    StepId = stepId,
                    StepType = stepType,
                    Status = StepStatus.Pending,
                }
            );
        }

        return workflowStepIds.Count;
    }

    /// <summary>清理实例相关资源：移除 _joinTrackers 和 _subWorkflowMappings 中属于该实例的条目</summary>
    private void CleanupInstanceResources(string instanceId)
    {
        _joinTrackers.TryRemove(instanceId, out _);

        // 移除属于该实例的子工作流映射（键格式为 "parentInstanceId:stepId"）
        // 先快照再删除，避免遍历期间集合被并发修改
        var prefix = $"{instanceId}:";
        var keysToRemove = _subWorkflowMappings.Keys
            .Where(key => key.StartsWith(prefix))
            .ToList();
        foreach (var key in keysToRemove)
            _subWorkflowMappings.TryRemove(key, out _);

        _logger.LogDebug("已清理实例资源: {InstanceId}", instanceId);
    }

    /// <summary>标记步骤完成</summary>
    private static void MarkCompleted(StepRecord record)
    {
        record.Status = StepStatus.Completed;
        record.CompletedAt = DateTime.UtcNow;
        record.Duration =
            record.StartedAt != default ? record.CompletedAt.Value - record.StartedAt : null;
    }

    /// <summary>标记步骤失败</summary>
    private static void MarkFailed(StepRecord record, string message, string? detail)
    {
        record.Status = StepStatus.Failed;
        record.CompletedAt = DateTime.UtcNow;
        record.Duration =
            record.StartedAt != default ? record.CompletedAt.Value - record.StartedAt : null;
        record.ErrorMessage = message;
        record.ErrorDetail = detail;
    }

    private string GetStepType(string stepId)
    {
        if (!_handlers.TryGetValue(stepId, out var handler))
            return "Unknown";
        return handler switch
        {
            AgentStepHandler => "Agent",
            HumanApprovalStepHandler => "Approval",
            CodeStepHandler => "Code",
            DelayStepHandler => "Delay",
            _ => "Custom"
        };
    }

    private static string BuildWebhookPayload(
        WorkflowContext ctx,
        string stepId,
        AgentStepHandler handler
    )
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
        WorkflowInstance instance,
        string completedStepId,
        CancellationToken ct
    )
    {
        if (!_joinTrackers.TryGetValue(instance.Context.InstanceId, out var tracker))
            return false;

        var remaining = Interlocked.Decrement(ref tracker.RemainingCount);

        // Any 模式：任一完成即推进
        if (tracker.WaitMode == ParallelWaitMode.Any)
        {
            // 标记已触发，后续步骤完成时幂等跳过
            if (Interlocked.CompareExchange(ref tracker.AnyTriggered, 1, 0) != 0)
            {
                // 已被其他步骤触发，幂等跳过
                _logger.LogDebug(
                    "ParallelJoinAny 已触发，跳过: {InstanceId}/{Step}",
                    instance.Context.InstanceId,
                    completedStepId
                );
                return true;
            }

            var anyDownstreamId = tracker.JoinDownstreamStepId;

            if (string.IsNullOrEmpty(anyDownstreamId))
            {
                // ParallelAny（无 JoinDownstream）：首个完成即标记，让步骤自行推进
                // 注意：不删除 tracker，保留 AnyTriggered=1 状态，防止后续步骤重复推进
                _logger.LogInformation(
                    "ParallelAny 完成: {InstanceId} (由 {Step} 触发)",
                    instance.Context.InstanceId,
                    completedStepId
                );
                return false; // 让调用方正常推进
            }

            _joinTrackers.TryRemove(instance.Context.InstanceId, out _);

            _logger.LogInformation(
                "ParallelJoinAny 完成: {InstanceId} → {Downstream} (由 {Step} 触发)",
                instance.Context.InstanceId,
                anyDownstreamId,
                completedStepId
            );

            await ExecuteStepAsync(instance, anyDownstreamId, triggeredBy: null, ct);
            return true;
        }

        // All 模式：等待所有子步骤完成
        if (remaining > 0)
        {
            _logger.LogDebug(
                "ParallelJoin 等待: {InstanceId}/{Step}, 剩余 {Remaining}/{Total} 个子步骤",
                instance.Context.InstanceId,
                completedStepId,
                remaining,
                tracker.TotalCount
            );
            return true; // 还没齐，调用方应 return
        }

        // 所有子步骤完成 → 推进到汇合步
        _joinTrackers.TryRemove(instance.Context.InstanceId, out _);
        var downstreamId = tracker.JoinDownstreamStepId;
        _logger.LogInformation(
            "ParallelJoin 完成: {InstanceId} → {Downstream}",
            instance.Context.InstanceId,
            downstreamId
        );

        await ExecuteStepAsync(instance, downstreamId, triggeredBy: null, ct);
        return true;
    }

    /// <summary>
    /// 取消指定步骤的恢复 Timer
    /// </summary>
    private void CancelRecoveryTimer(string instanceId, string stepId)
    {
        var timerKey = $"{instanceId}:{stepId}";
        if (_recoveryTimers.TryRemove(timerKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// 启动恢复超时 Timer。5 分钟后若步骤仍为 Recovering，则重发 webhook。
    /// HumanApprovalStep 不启动此 Timer（静默等待）。
    /// </summary>
    private void StartRecoveryTimer(string instanceId, string stepId, string? inputSnapshot)
    {
        var timerKey = $"{instanceId}:{stepId}";
        var cts = new CancellationTokenSource();
        _recoveryTimers[timerKey] = cts;

        var task = RecoveryTimerLoopAsync(instanceId, stepId, inputSnapshot, timerKey, cts.Token);
        _recoveryTasks[timerKey] = task;
    }

    private async Task RecoveryTimerLoopAsync(
        string instanceId, string stepId, string? inputSnapshot,
        string timerKey, CancellationToken ct)
    {
        try
        {
            await Task.Delay(RecoveryTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            return; // 被取消则跳过（回调已到达并完成了处理）
        }

        _recoveryTimers.TryRemove(timerKey, out var _);
        _recoveryTasks.TryRemove(timerKey, out var _);

        if (!_instances.TryGetValue(instanceId, out var instance))
            return;

        var record = instance.StepRecords.FirstOrDefault(r => r.StepId == stepId);
        if (record is null)
            return;

        if (record is { Status: StepStatus.Completed or StepStatus.Failed })
        {
            _logger.LogDebug("恢复 Timer 过期但步骤已完成: {InstanceId}/{Step}", instanceId, stepId);
            return;
        }

        // 仍为 Recovering → 重发 webhook
        _logger.LogWarning("恢复超时，重发 webhook: {InstanceId}/{Step}", instanceId, stepId);

        try
        {
            if (string.IsNullOrEmpty(inputSnapshot))
            {
                _logger.LogError("无法重发 webhook: {InstanceId}/{Step} InputSnapshot 为空", instanceId, stepId);
                return;
            }

            if (!_handlers.TryGetValue(stepId, out var handler) || handler is not AgentStepHandler agentHandler)
            {
                _logger.LogError("无法重发 webhook: {InstanceId}/{Step} 找不到 AgentStepHandler", instanceId, stepId);
                return;
            }

            // 获取分布式锁，防止多实例同时重发
            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            opCts.CancelAfter(RecoveryOpTimeout);
            await using var distributedLock = await TryAcquireDistributedLockAsync(instanceId, opCts.Token);

            // 获取锁后重新检查状态 —— 回调可能已在等待锁期间完成该步骤
            if (record is { Status: StepStatus.Completed or StepStatus.Failed })
            {
                _logger.LogDebug("获取锁后发现步骤已完成，跳过重发: {InstanceId}/{Step}", instanceId, stepId);
                return;
            }

            // 从 StepDefinition 或 Handler 解析 RouteName/EventType（YAML 可覆盖 Handler 虚属性）
            var recoveryStepDef = GetStepDefinition(instance.WorkflowName, stepId);
            var recoveryRouteName = !string.IsNullOrWhiteSpace(recoveryStepDef?.RouteName) ? recoveryStepDef!.RouteName : agentHandler.RouteName;
            var recoveryEventType = !string.IsNullOrWhiteSpace(recoveryStepDef?.EventType) ? recoveryStepDef!.EventType : agentHandler.EventType;

            await _webhookClient.SendRawAsync(
                recoveryRouteName,
                recoveryEventType,
                inputSnapshot,
                ct: opCts.Token);

            record.Status = StepStatus.Dispatched;
            record.StartedAt = DateTime.UtcNow;

            await SaveCheckpointAsync(instance, opCts.Token);

            _logger.LogInformation("重发 webhook 成功: {InstanceId}/{Step}", instanceId, stepId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重发 webhook 失败: {InstanceId}/{Step}", instanceId, stepId);
        }
    }

    /// <summary>
    /// 尝试获取分布式锁（仅当使用 RedisStateStore 时）。
    /// 单实例部署或 Sqlite 场景返回 null（无锁）。
    /// </summary>
    private async Task<IAsyncDisposable?> TryAcquireDistributedLockAsync(
        string instanceId,
        CancellationToken ct
    )
    {
        if (_stateStore is RedisStateStore redisStore)
        {
            try
            {
                var lockObj = await redisStore.AcquireLockAsync(instanceId, ct);
                _logger.LogDebug("获取分布式锁: {InstanceId}", instanceId);
                return lockObj;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取分布式锁失败，继续处理回调（降级为进程内锁）: {InstanceId}", instanceId);
                return null;
            }
        }

        // Sqlite 或 InMemory 场景：无需分布式锁
        return null;
    }

    /// <summary>
    /// ParallelJoin 追踪器 — 管理并行子步骤的完成计数。
    /// </summary>
    private class JoinTracker
    {
        public string JoinDownstreamStepId { get; init; } = "";
        public int TotalCount { get; init; }
        public int RemainingCount;
        public ParallelWaitMode WaitMode { get; init; } = ParallelWaitMode.All;
        public int AnyTriggered; // 0=未触发, 1=已触发（CompareExchange 原子操作）
    }
}

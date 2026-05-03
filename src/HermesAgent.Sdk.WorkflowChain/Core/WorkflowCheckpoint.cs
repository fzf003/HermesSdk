using System.Text.Json;
using System.Text.Json.Serialization;

namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流检查点 — 可序列化的完整快照。
/// 用于持久化和重启恢复。
/// </summary>
public class WorkflowCheckpoint
{
    private const string TypePropertyName = "$type";
    private const string ValuePropertyName = "$value";

    /// <summary>工作流实例唯一标识</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>入口步骤 ID</summary>
    public string EntryStepId { get; set; } = "";

    /// <summary>工作流状态：running | timed-out | completed | failed</summary>
    public string Status { get; set; } = "running";

    /// <summary>创建时间</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>最后一次心跳时间</summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>工作流启动时的输入参数</summary>
    public Dictionary<string, JsonElement> InitialInput { get; set; } = new();

    /// <summary>各步骤的输出结果集 stepId → output（字符串化）</summary>
    public Dictionary<string, JsonElement> StepOutputs { get; set; } = new();

    /// <summary>自定义共享数据（字符串化）</summary>
    public Dictionary<string, JsonElement> Data { get; set; } = new();

    /// <summary>是否应继续执行</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>当前正在等待回调的步骤 ID 集合</summary>
    public List<string> PendingStepIds { get; set; } = new();

    /// <summary>已发出 Webhook 但尚未收到回调的 Agent 步骤 ID 集合</summary>
    public List<string> InFlightStepIds { get; set; } = new();

    /// <summary>所有步骤的执行档案</summary>
    public List<StepRecord> StepRecords { get; set; } = new();

    /// <summary>从 WorkflowInstance 创建检查点</summary>
    public static WorkflowCheckpoint FromInstance(WorkflowInstance instance)
    {
        var ctx = instance.Context;

        return new WorkflowCheckpoint
        {
            InstanceId = ctx.InstanceId,
            EntryStepId = instance.EntryStepId,
            Status = instance.Status,
            StartedAt = instance.StartedAt,
            CompletedAt = instance.CompletedAt,
            LastHeartbeat = DateTime.UtcNow,

            InitialInput = SnapshotDictionary(ctx.InitialInput),
            StepOutputs = SnapshotDictionary(ctx.StepOutputs),
            Data = SnapshotDictionary(ctx.Data),
            IsRunning = ctx.IsRunning,

            PendingStepIds = ctx.PendingStepIds.ToList(),
            InFlightStepIds = instance.InFlightStepIds.ToList(),

            StepRecords = instance.StepRecords.Select(r => new StepRecord
            {
                StepId = r.StepId,
                StepType = r.StepType,
                Status = r.Status,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Duration = r.Duration,
                ErrorMessage = r.ErrorMessage,
                ErrorDetail = r.ErrorDetail,
                FullStackTrace = r.FullStackTrace,
                TriggeredBy = r.TriggeredBy,
                TriggeredSteps = r.TriggeredSteps?.ToList(),
                InputSnapshot = r.InputSnapshot,
                OutputSnapshot = r.OutputSnapshot,
            }).ToList(),
        };
    }

    /// <summary>重建 WorkflowInstance</summary>
    public WorkflowInstance ToInstance()
    {
        var ctx = new WorkflowContext
        {
            InstanceId = InstanceId,
            InitialInput = RestoreDictionary(InitialInput),
            IsRunning = IsRunning,
        };

        foreach (var (key, value) in StepOutputs)
            ctx.StepOutputs[key] = RestoreValue(value);
        foreach (var (key, value) in Data)
            ctx.Data[key] = RestoreValue(value);
        foreach (var id in PendingStepIds)
            ctx.PendingStepIds.Add(id);

        var instance = new WorkflowInstance
        {
            Context = ctx,
            EntryStepId = EntryStepId,
            Status = Status,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
        };

        foreach (var r in StepRecords)
        {
            instance.StepRecords.Add(new StepRecord
            {
                StepId = r.StepId,
                StepType = r.StepType,
                Status = r.Status,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Duration = r.Duration,
                ErrorMessage = r.ErrorMessage,
                ErrorDetail = r.ErrorDetail,
                FullStackTrace = r.FullStackTrace,
                TriggeredBy = r.TriggeredBy,
                TriggeredSteps = r.TriggeredSteps?.ToList(),
                InputSnapshot = r.InputSnapshot,
                OutputSnapshot = r.OutputSnapshot,
            });
        }

        foreach (var id in InFlightStepIds)
            instance.InFlightStepIds.Add(id);

        return instance;
    }

    private static Dictionary<string, JsonElement> SnapshotDictionary(
        IReadOnlyDictionary<string, object?> source)
        => source.ToDictionary(kv => kv.Key, kv => ToJsonElement(kv.Value));

    private static Dictionary<string, object?> RestoreDictionary(
        IReadOnlyDictionary<string, JsonElement> source)
        => source.ToDictionary(kv => kv.Key, kv => RestoreValue(kv.Value));

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is null)
            return JsonSerializer.SerializeToElement<object?>(null);

        var runtimeType = value.GetType();
        var payload = value is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(value, runtimeType);

        return JsonSerializer.SerializeToElement(new TypedValueEnvelope
        {
            Type = runtimeType.AssemblyQualifiedName,
            Value = payload,
        });
    }

    private static object? RestoreValue(JsonElement element)
    {
        if (TryRestoreTypedValue(element, out var restored))
            return restored;

        return RestoreLegacyValue(element);
    }

    private static bool TryRestoreTypedValue(JsonElement element, out object? value)
    {
        value = null;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(TypePropertyName, out var typeElement) ||
            !element.TryGetProperty(ValuePropertyName, out var valueElement))
        {
            return false;
        }

        var typeName = typeElement.GetString();
        if (string.IsNullOrWhiteSpace(typeName))
        {
            value = valueElement.ValueKind == JsonValueKind.Null ? null : RestoreLegacyValue(valueElement);
            return true;
        }

        var runtimeType = Type.GetType(typeName, throwOnError: false);
        if (runtimeType == null)
            return false;

        if (runtimeType == typeof(JsonElement))
        {
            value = valueElement.Clone();
            return true;
        }

        if (valueElement.ValueKind == JsonValueKind.Null)
        {
            value = null;
            return true;
        }

        value = JsonSerializer.Deserialize(valueElement.GetRawText(), runtimeType);
        return true;
    }

    private static object? RestoreLegacyValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => RestoreValue(property.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(RestoreValue)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null,
        };

    private sealed class TypedValueEnvelope
    {
        [JsonPropertyName("$type")]
        public string? Type { get; init; }
        [JsonPropertyName("$value")]
        public JsonElement Value { get; init; }
    }
}

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

    /// <summary>
    /// 类型别名映射表 - 用于解决程序集版本变更导致的反序列化问题。
    /// 格式: "简短别名" -> "完整类型名"
    /// </summary>
    private static readonly Dictionary<string, string> TypeAliasMap = new()
    {
        // 常用类型的简短别名
        { "string", typeof(string).AssemblyQualifiedName! },
        { "int", typeof(int).AssemblyQualifiedName! },
        { "long", typeof(long).AssemblyQualifiedName! },
        { "double", typeof(double).AssemblyQualifiedName! },
        { "bool", typeof(bool).AssemblyQualifiedName! },
        { "datetime", typeof(DateTime).AssemblyQualifiedName! },
        { "timespan", typeof(TimeSpan).AssemblyQualifiedName! },
        { "guid", typeof(Guid).AssemblyQualifiedName! },
        { "decimal", typeof(decimal).AssemblyQualifiedName! },
        
        // 项目内部类型（使用简化的命名空间）
        { "workflowcontext", "HermesAgent.Sdk.WorkflowChain.WorkflowContext" },
        { "steprecord", "HermesAgent.Sdk.WorkflowChain.StepRecord" },
        { "stepstatus", "HermesAgent.Sdk.WorkflowChain.StepStatus" },
    };

    /// <summary>
    /// FullName→alias 反查表（懒加载 + 线程安全），避免 GetSimplifiedTypeName 每次遍历整个 TypeAliasMap。
    /// RegisterTypeAlias 时重建 Lazy 实例确保失效。
    /// </summary>
    private static Lazy<Dictionary<string, string>> _fullNameToAlias = new(BuildAliasMap, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dictionary<string, string> FullNameToAliasMap => _fullNameToAlias.Value;

    private static Dictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var kvp in TypeAliasMap)
        {
            // TypeAliasMap 值可能是 AssemblyQualifiedName 或简短命名空间
            // 提取 FullName 部分（第一个逗号前）作为反查键
            var value = kvp.Value;
            var commaIdx = value.IndexOf(',');
            var fullNameKey = commaIdx > 0 ? value.Substring(0, commaIdx).Trim() : value;
            map[fullNameKey] = kvp.Key;
        }
        return map;
    }

    /// <summary>
    /// 注册自定义类型别名。
    /// </summary>
    /// <param name="alias">简短别名</param>
    /// <param name="typeName">完整类型名（AssemblyQualifiedName）</param>
    public static void RegisterTypeAlias(string alias, string typeName)
    {
        TypeAliasMap[alias.ToLowerInvariant()] = typeName;
        // 重建 Lazy 实例，下次访问时重新构建反查表
        _fullNameToAlias = new Lazy<Dictionary<string, string>>(BuildAliasMap, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 根据别名或类型名解析类型。
    /// </summary>
    private static Type? ResolveType(string typeName)
    {
        // 先尝试直接解析
        var type = Type.GetType(typeName, throwOnError: false);
        if (type != null)
            return type;

        // 尝试从别名映射表中查找
        var lowerName = typeName.ToLowerInvariant();
        if (TypeAliasMap.TryGetValue(lowerName, out var mappedTypeName))
        {
            type = Type.GetType(mappedTypeName, throwOnError: false);
            if (type != null)
                return type;
        }

        // 尝试忽略程序集版本信息
        var commaIndex = typeName.IndexOf(',');
        if (commaIndex > 0)
        {
            var simpleName = typeName.Substring(0, commaIndex).Trim();
            type = Type.GetType(simpleName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

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

    /// <summary>子工作流映射："parentInstanceId:subWorkflowStepId" → "subWorkflowInstanceId"</summary>
    public Dictionary<string, string> SubWorkflowMappings { get; set; } = new();

    /// <summary>从 WorkflowInstance 创建检查点</summary>
    public static WorkflowCheckpoint FromInstance(WorkflowInstance instance, Dictionary<string, string>? subWorkflowMappings = null)
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

            SubWorkflowMappings = subWorkflowMappings ?? new(),
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

        // 使用简化的类型名（不包含程序集版本信息）
        var simplifiedTypeName = GetSimplifiedTypeName(runtimeType);

        return JsonSerializer.SerializeToElement(new TypedValueEnvelope
        {
            Type = simplifiedTypeName,
            Value = payload,
        });
    }

    /// <summary>
    /// 获取简化的类型名（不含程序集版本信息）。
    /// </summary>
    private static string GetSimplifiedTypeName(Type type)
    {
        // 使用反查表 O(1) 查找，替代遍历 O(n)
        if (type.FullName != null && FullNameToAliasMap.TryGetValue(type.FullName, out var alias))
            return alias;

        // 对于其他类型，使用 AssemblyQualifiedName 确保正确反序列化
        var assemblyQualifiedName = type.AssemblyQualifiedName;
        if (string.IsNullOrEmpty(assemblyQualifiedName))
            return type.FullName ?? type.Name;

        // 包含 [[ 说明是复杂泛型类型（如 Dictionary`2[[...]]），
        // 剥离版本信息会导致 Type.GetType() 无法解析，保留完整信息
        if (assemblyQualifiedName.Contains("[["))
            return assemblyQualifiedName;

        // 移除程序集版本、文化、公钥等信息，只保留 "命名空间.类名, 程序集名"
        var commaIndex = assemblyQualifiedName.IndexOf(',');
        if (commaIndex > 0)
        {
            var typeNamePart = assemblyQualifiedName.Substring(0, commaIndex);
            var assemblyNamePart = assemblyQualifiedName.Substring(commaIndex + 1);

            // 提取程序集简单名称（第一个逗号之前）
            var assemblySimpleName = assemblyNamePart.Split(',')[0].Trim();

            return $"{typeNamePart}, {assemblySimpleName}";
        }

        return assemblyQualifiedName;
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

        // 使用新的类型解析机制（支持别名和版本容错）
        var runtimeType = ResolveType(typeName);
        if (runtimeType == null)
        {
            // 如果无法解析类型，记录警告并尝试作为 JsonElement 返回
            System.Diagnostics.Debug.WriteLine($"警告: 无法解析类型 '{typeName}'，将作为原始 JSON 返回");
            return false;
        }

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

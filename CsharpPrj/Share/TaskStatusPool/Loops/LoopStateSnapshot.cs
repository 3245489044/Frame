using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 快照序列化共享机制（SingleImplementation）：JSON 安全归一化 + JsonElement → CLR 值还原。
/// 收敛 Agent.State.SchedulerStateSnapshotSerializer 与 Agent.Persistence.SnapshotSerializer 两套快照序列化器
/// 重复手写的 SanitizeData/Unwrap（消除 ANTI-DUPLICATED-IMPLEMENTATION 与 Unwrap 语义分叉）；
/// JsonScalar/JsonValueToObject 为通用 JSON 工具单一实现（Agent.State.JsonShim 委托本类）。
/// 本类只依赖统一上下文原语（同库 ILoopState），Agent 无关。
/// </summary>
public static class LoopStateSnapshot
{
    /// <summary>把 JsonElement 标量归一为原始 CLR 标量（string/long/double/bool）；对象/数组等返回 null，由调用方自行处理。</summary>
    public static object? JsonScalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? string.Empty,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        _ => null,
    };

    /// <summary>把 JsonElement 递归还原为原始 CLR 对象（标量 / 字典 / 列表），供快照还原 / 工作流参数构建消费。</summary>
    public static object JsonValueToObject(JsonElement e)
    {
        if (JsonScalar(e) is { } scalar) return scalar;
        return e.ValueKind switch
        {
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => JsonValueToObject(p.Value)),
            JsonValueKind.Array => e.EnumerateArray().Select(JsonValueToObject).ToList(),
            _ => e.ToString(),
        };
    }

    /// <summary>把 JSON 反序列化产生的 <see cref="JsonElement"/> 还原为原始 CLR 类型（标量；对象/数组递归还原为字典/列表），保证快照还原后结构与类型不漂移。</summary>
    public static object Unwrap(object value)
    {
        if (value is not JsonElement je) return value;
        return JsonValueToObject(je);
    }

    /// <summary>对数据字典做 JSON 安全归一化（标量 / 可 JSON 化保留，否则降级为类型名标记），保证快照可落库。</summary>
    public static Dictionary<string, object?> SanitizeData(IEnumerable<KeyValuePair<string, object?>> data)
    {
        var result = new Dictionary<string, object?>();
        if (data == null) return result;
        foreach (var kv in data)
        {
            var value = kv.Value;
            // 标量 / 可 JSON 化对象保留；否则降级为类型名标记，保证快照可落库。
            if (value is null || value is string || value is int || value is long || value is double
                || value is bool || value is DateTime || value is decimal || value is Guid
                || value is System.Collections.IEnumerable && value is not string)
            {
                result[kv.Key] = value;
            }
            else
            {
                result[kv.Key] = $"<unserializable:{value.GetType().FullName}>";
            }
        }
        return result;
    }
}

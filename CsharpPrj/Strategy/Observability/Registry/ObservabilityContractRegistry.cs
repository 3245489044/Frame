using System.Collections.Concurrent;
using System.Text.Json;
using Strategy.Observability.Envelope;

namespace Strategy.Observability.Registry;

/// <summary>契约注册端口（E3）。</summary>
public interface IObservabilityContractRegistry
{
    /// <summary>装配期注册；重复注册返回 false（防重复）。</summary>
    bool Register(EventContract contract);

    /// <summary>Emit 前校验；返回是否有问题（未注册/字段缺失/度量缺失/枚举违规）。</summary>
    bool Validate(ObservabilityEvent evt, out string? issue);

    bool IsRegistered(string eventKind);

    IReadOnlyList<EventContract> Contracts { get; }
}

/// <summary>内存契约注册表实现（E3）：并发安全，装载全部契约并执行校验。</summary>
public sealed class ObservabilityContractRegistry : IObservabilityContractRegistry
{
    private readonly ConcurrentDictionary<string, EventContract> _byKind = new(StringComparer.Ordinal);

    public bool Register(EventContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.EventKind)) throw new ArgumentException("EventKind 不能为空", nameof(contract));
        return _byKind.TryAdd(contract.EventKind, contract);
    }

    public bool IsRegistered(string eventKind) => _byKind.ContainsKey(eventKind);

    public IReadOnlyList<EventContract> Contracts => _byKind.Values.OrderBy(c => c.EventKind, StringComparer.Ordinal).ToList();

    public bool Validate(ObservabilityEvent evt, out string? issue)
    {
        issue = null;
        if (!_byKind.TryGetValue(evt.EventKind, out var c))
        {
            issue = $"未注册的事件分类 '{evt.EventKind}'（新增观测点请先注册契约）";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(c.EntityType) && !string.Equals(c.EntityType, evt.Entity.EntityType, StringComparison.Ordinal))
        {
            issue = $"{evt.EventKind} 期望实体类型 '{c.EntityType}'，实际 '{evt.Entity.EntityType}'";
            return false;
        }

        if (c.Required.Count > 0)
        {
            var missing = MissingRequiredPayloadFields(evt.PayloadJson, c.Required);
            if (missing.Count > 0)
            {
                issue = $"{evt.EventKind} 缺少必填载荷字段: {string.Join(", ", missing)}";
                return false;
            }
        }

        if (c.MetricKeys.Count > 0)
        {
            var missing = c.MetricKeys.Where(k => evt.Metrics == null || !evt.Metrics.ContainsKey(k)).ToList();
            if (missing.Count > 0)
            {
                issue = $"{evt.EventKind} 缺少声明度量键: {string.Join(", ", missing)}";
                return false;
            }
        }

        if (c.Enums.Count > 0)
        {
            var payload = ParsePayload(evt.PayloadJson);
            if (payload is JsonElement root && root.ValueKind == JsonValueKind.Object)
            {
                foreach (var (field, allowed) in c.Enums)
                {
                    if (TryGetPropertyIgnoreCase(root, field, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        if (!allowed.Contains(v.GetString()!))
                        {
                            issue = $"{evt.EventKind}.{field} 取值 '{v.GetString()}' 不在允许集合中";
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private static IReadOnlyList<string> MissingRequiredPayloadFields(string? payloadJson, IReadOnlyList<string> required)
    {
        if (payloadJson is null) return required;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return required;
            return required.Where(f => !HasPropertyIgnoreCase(root, f)).ToList();
        }
        catch (JsonException)
        {
            return required;
        }
    }

    /// <summary>大小写不敏感判定 JSON 对象是否含某属性（对齐载荷 camelCase 与契约任意大小写声明）。</summary>
    private static bool HasPropertyIgnoreCase(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value.Clone();
            return true;
        }
        value = default;
        return false;
    }

    private static JsonElement? ParsePayload(string? payloadJson)
    {
        if (payloadJson is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}


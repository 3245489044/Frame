using System.Globalization;
using System.Text.Json;
using Strategy.Observability.Envelope;

namespace Strategy.Observability.Projection;

/// <summary>
/// 观测产物（E13）：事件信封 / 端口返回值 / 直接值的<b>统一出证载荷</b>。
/// 断言取值根由此对象暴露（字段 / 度量 / 值），内核不解释语义。
/// </summary>
public sealed class ObservationOutcome
{
    /// <summary>分类（事件分类 / 端口键 / 显式标签）。</summary>
    public string? Kind { get; init; }

    /// <summary>阶段（事件信封 Stage；如 completed / failed）。</summary>
    public string? Status { get; init; }

    /// <summary>运行标识（关联维度）。</summary>
    public string? RunId { get; init; }

    /// <summary>轮次标识（关联维度）。</summary>
    public string? RoundId { get; init; }

    /// <summary>实体类型（事件信封 Entity.EntityType）。</summary>
    public string? EntityType { get; init; }

    /// <summary>度量（数值键）。</summary>
    public IReadOnlyDictionary<string, double> Metrics { get; init; }
        = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>载荷字段（键 Ordinal；值为 JSON 反序列化后的普通对象）。</summary>
    public IReadOnlyDictionary<string, object?> Fields { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>返回值（端口调用 / 直接值的投影根；事件投影为 null）。</summary>
    public object? Value { get; init; }

    /// <summary>由事件信封投影（载荷 JSON 展开为字段表）。</summary>
    public static ObservationOutcome FromEvent(ObservabilityEvent evt, string? runId = null, string? roundId = null)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return new ObservationOutcome
        {
            Kind = evt.EventKind,
            Status = evt.Stage,
            RunId = runId,
            RoundId = roundId,
            EntityType = evt.Entity.EntityType,
            Metrics = evt.Metrics is null
                ? new Dictionary<string, double>(StringComparer.Ordinal)
                : new Dictionary<string, double>(evt.Metrics, StringComparer.Ordinal),
            Fields = Flatten(evt.PayloadJson),
        };
    }

    /// <summary>由直接值投影（端口返回值 / 计算产物）。</summary>
    public static ObservationOutcome FromValue(object? value, string? kind = null)
        => new() { Kind = kind, Value = value };

    /// <summary>取度量（未声明 → null）。</summary>
    public double? Metric(string key)
        => Metrics.TryGetValue(key, out var value) ? value : null;

    /// <summary>取载荷字段（键匹配忽略大小写与 <c>_</c>/<c>-</c>，对齐用例 path 书写自由度）。</summary>
    public object? Field(string name)
    {
        if (Fields.TryGetValue(name, out var exact)) return exact;
        var normalized = Normalize(name);
        foreach (var (key, value) in Fields)
            if (string.Equals(Normalize(key), normalized, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    /// <summary>字段是否存在。</summary>
    public bool HasField(string name) => Field(name) is not null;

    /// <summary>字段文本化（不变量格式；null → null）。</summary>
    public string? Text(string name) => TextOf(Field(name));

    /// <summary>不变量文本化（null → null；bool → True/False；数值 → 不变量）。</summary>
    public static string? TextOf(object? value)
        => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "True" : "False",
            JsonElement element => element.ValueKind == JsonValueKind.Null ? null : element.ToString(),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

    /// <summary>载荷 JSON → 字段表（非对象载荷归入 <c>value</c> 键；解析失败 → 空表）。</summary>
    private static IReadOnlyDictionary<string, object?> Flatten(string? payloadJson)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(payloadJson)) return fields;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                fields["value"] = Materialize(root);
                return fields;
            }

            foreach (var property in root.EnumerateObject())
                fields[property.Name] = Materialize(property.Value);
        }
        catch (JsonException)
        {
            // 载荷非 JSON（原始字符串）：整体归入 value 键，保持可断言。
            fields["value"] = payloadJson;
        }

        return fields;
    }

    /// <summary>JSON 值 → 普通对象（脱离 JsonDocument 生命周期）。</summary>
    private static object? Materialize(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer : element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(Materialize).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => Materialize(p.Value), StringComparer.Ordinal),
            _ => element.ToString(),
        };

    private static string Normalize(string name)
        => name.Replace("_", string.Empty, StringComparison.Ordinal)
               .Replace("-", string.Empty, StringComparison.Ordinal);
}

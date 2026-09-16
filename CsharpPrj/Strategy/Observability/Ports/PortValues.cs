using System.Reflection;
using System.Text.Json;

namespace Strategy.Observability.Ports;

/// <summary>
/// 取值小件单源（组合绑定 / 脚本绑定 / 实参构造共用）：属性路径取值、计数、JSON 归一、文本形式。
/// <para>分工：只做纯值变换，不做任何诊断抛错（不可计数 → 返回 null，由调用方按其上下文给出可定位异常）。</para>
/// </summary>
internal static class PortValues
{
    /// <summary>属性路径取值（<c>A.B.C</c>：逐段属性优先、回落字段、忽略大小写；任一段缺失 → null）。</summary>
    internal static object? PathOf(object? value, string path)
    {
        var current = value;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null) return null;

            var type = current.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            var member = (MemberInfo?)type.GetProperty(segment, flags)
                         ?? type.GetProperties(flags).FirstOrDefault(p =>
                             string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase))
                         ?? type.GetField(segment, flags)
                         ?? (MemberInfo?)type.GetFields(flags).FirstOrDefault(f =>
                             string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

            current = member switch
            {
                PropertyInfo property => property.GetValue(current),
                FieldInfo field => field.GetValue(current),
                _ => null,
            };
        }

        return current;
    }

    /// <summary>计数（集合 / 数组 / 字符串长度；空 → 0；不可计数 → null）。</summary>
    internal static int? TryCount(object? value)
        => value switch
        {
            null => 0,
            string text => text.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable sequence => CountByIteration(sequence),
            _ => null,
        };

    /// <summary>文本形式（null → null；JSON 元素按值语义取文本：字符串取值、结构取原文；其余 <c>ToString</c>）。</summary>
    internal static string? Text(object? value) => value switch
    {
        null => null,
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText(),
        _ => value.ToString(),
    };

    /// <summary>条件真值（null / false / 空串 / 空 JSON / 零数 → 假；其余 → 真）。</summary>
    internal static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0 && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase),
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null => false,
            JsonValueKind.String => (element.GetString() ?? string.Empty).Length > 0,
            JsonValueKind.Number => element.GetDouble() != 0,
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            _ => false,
        },
        _ => true,
    };

    /// <summary>数值形式（数值 / 数值文本 → <c>decimal</c>；其余 → null）。</summary>
    internal static decimal? Number(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case decimal decimalValue:
                return decimalValue;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double:
                return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDecimal(out var parsed) ? parsed : null;
            case string text:
                return decimal.TryParse(
                    text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;
            default:
                return null;
        }
    }

    /// <summary>JSON 值 → 原义值（标量取 CLR 值；null / undefined → null；结构保留元素）。</summary>
    internal static object? Unwrap(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            _ => element.Clone(),
        };

    /// <summary>JSON 值 → 目标类型（<c>JsonElement</c>/<c>object</c> 位保留元素；null / undefined → null）。</summary>
    internal static object? Coerce(object? value, Type target)
    {
        if (value is not JsonElement element) return value;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (target == typeof(JsonElement) || target == typeof(object)) return element.Clone();

        return JsonSerializer.Deserialize(element.GetRawText(), target);
    }

    /// <summary>
    /// 位置实参**对象直投**判定（对象实参通道单源）：<c>convert</c> 显式为 <c>object</c>/<c>passthrough</c>；
    /// 或 <c>convert</c> 缺省且实参本身**非文本载体**（非 <c>string</c> / 非 <c>JsonElement</c>）——
    /// 后者为调用方直传对象的形态（如产品组合根注入的实例），不得被 <c>ToString</c> 文本化。
    /// <para>null 实参不属直投（缺参语义仍由调用方按 <c>optional</c>/<c>default</c> 裁决）。</para>
    /// </summary>
    internal static bool IsDirectPositional(string? convert, object? raw)
        => raw is not null
           && (convert is "object" or "passthrough"
               || (convert is null or "" && raw is not (string or JsonElement)));

    /// <summary>直投值的目标类型兼容性（<c>object</c> 位恒兼容；null 由调用方先行裁决）。</summary>
    internal static bool FitsDirect(object? value, Type target)
        => target == typeof(object) || value is null || target.IsInstanceOfType(value);

    /// <summary>序列展开为元素集（非序载体 → 单元素；null → 空集，与夹具 <c>?? Array.Empty&lt;T&gt;()</c> 口径一致）。</summary>
    internal static IEnumerable<object?> Elements(object? value)
    {
        switch (value)
        {
            case null:
                return Array.Empty<object?>();
            case string text:
                return text.Select(c => (object?)c.ToString());
            case System.Collections.IEnumerable sequence:
                return sequence.Cast<object?>();
            default:
                return new[] { value };
        }
    }

    /// <summary>对象属性 / 字典逐项展开（供 <c>spread</c> 动态键写入产物）。</summary>
    internal static IReadOnlyDictionary<string, object?> Spread(object? value)
    {
        var entries = new Dictionary<string, object?>(StringComparer.Ordinal);

        switch (value)
        {
            case null:
                return entries;

            case IReadOnlyDictionary<string, object?> readOnly:
                foreach (var (key, item) in readOnly) entries[key] = item;
                return entries;

            case System.Collections.IDictionary dictionary:
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                    if (entry.Key is not null) entries[entry.Key.ToString()!] = entry.Value;
                return entries;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in value.GetType().GetProperties(flags))
            entries[property.Name] = property.GetValue(value);

        return entries;
    }

    private static int CountByIteration(System.Collections.IEnumerable sequence)
    {
        var count = 0;
        foreach (var _ in sequence) count++;
        return count;
    }

    /// <summary>
    /// JSON → 通用对象图（数组 → <c>List&lt;object?&gt;</c>、对象 → <c>Dictionary&lt;string,object?&gt;</c>、标量 → CLR 值）。
    /// <para>用途：声明面以 <c>convert: graph</c> 取值时无需写出 CLR 类型全名即可遍历 / 取下标 / 取属性。</para>
    /// </summary>
    internal static object? ToGraph(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().Select(ToGraph).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ToGraph(property.Value), StringComparer.Ordinal),
            _ => Unwrap(element),
        };
}

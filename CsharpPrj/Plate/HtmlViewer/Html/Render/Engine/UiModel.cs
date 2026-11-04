#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace UiBackend.Html;

/// <summary>
/// 行模型单源（模板 / 表达式 / 投影器共用）—— 收敛大小写不敏感字典、<c>"1"/""</c> 布尔串、
/// 取值/路径、真值语义与模型值归一化。
/// <para>纪律：模型字段的"真/假"只经 <see cref="Flag"/>/<see cref="Truthy"/> 表达；
/// 原生 bool 不得直接进模板（否则 <c>false.ToString()</c> 会被当非空真值）。</para>
/// </summary>
public static class UiModel
{
    /// <summary>空模型（只读单例）。</summary>
    public static IReadOnlyDictionary<string, object?> Empty { get; } = new Dictionary<string, object?>();

    /// <summary>新建大小写不敏感模型；<paramref name="extra"/> 逐项并入（同键以后者为准）。</summary>
    public static Dictionary<string, object?> New(IReadOnlyDictionary<string, object?>? extra = null)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (extra is not null)
        {
            foreach (var kv in extra) model[kv.Key] = kv.Value;
        }

        return model;
    }

    /// <summary>布尔 → 模型标志串（真 = <c>"1"</c>，假 = 空串）。</summary>
    public static string Flag(bool value) => value ? "1" : string.Empty;

    /// <summary>写入布尔标志（键存在即覆盖）。</summary>
    public static void SetFlag(IDictionary<string, object?> model, string key, bool value)
    {
        ArgumentNullException.ThrowIfNull(model);
        model[key] = Flag(value);
    }

    /// <summary>任意值 → 模板可比较的模型值（bool → <c>"1"/""</c>；数值 → 不变文化文本）。</summary>
    public static object? ToModelValue(object? value) => value switch
    {
        null => null,
        bool b => b ? "1" : string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value
    };

    /// <summary>取值（键大小写不敏感）；返回模型值文本，缺失 ⇒ null。</summary>
    public static string? Get(IReadOnlyDictionary<string, object?> model, string field)
    {
        foreach (var kv in model)
        {
            if (string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
                return ToText(ToModelValue(kv.Value));
        }

        return null;
    }

    /// <summary>取列表（键大小写不敏感）；值非序列 ⇒ false。</summary>
    public static bool TryGetList(
        IReadOnlyDictionary<string, object?> model,
        string name,
        out IEnumerable<object?> list)
    {
        foreach (var kv in model)
        {
            if (!string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Value is IEnumerable<object?> seq)
            {
                list = seq;
                return true;
            }

            break;
        }

        list = Array.Empty<object?>();
        return false;
    }

    /// <summary>列表项 → 行模型（字典项原样；标量项暴露 <c>{this}</c>）。</summary>
    public static IReadOnlyDictionary<string, object?> ToModel(object? item)
    {
        if (item is IReadOnlyDictionary<string, object?> dict) return dict;
        if (item is not null)
            return new Dictionary<string, object?> { ["this"] = item };
        return Empty;
    }

    /// <summary>真值（模板条件/表达式共用）：null/空白/<c>"0"</c>/<c>"false"</c>/零/空集合为假。</summary>
    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s)
                    && !string.Equals(s, "0", StringComparison.Ordinal)
                    && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        float f => f != 0,
        decimal m => m != 0,
        System.Collections.ICollection c => c.Count > 0,
        _ => true
    };

    /// <summary>值 → 文本（不变文化；null ⇒ 空串）。</summary>
    public static string ToText(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>模型路径取值（<c>a.b</c>，大小写不敏感）；缺失 ⇒ null。</summary>
    public static object? Lookup(IReadOnlyDictionary<string, object?> model, string path)
    {
        object? current = model;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = current switch
            {
                IReadOnlyDictionary<string, object?> readOnly => Find(readOnly, segment),
                IDictionary<string, object?> mutable => Find(mutable, segment),
                _ => null
            };
            if (current is null) return null;
        }

        return current;
    }

    private static object? Find(IReadOnlyDictionary<string, object?> model, string key)
    {
        foreach (var kv in model)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }

        return null;
    }

    private static object? Find(IDictionary<string, object?> model, string key)
    {
        foreach (var kv in model)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }

        return null;
    }
}

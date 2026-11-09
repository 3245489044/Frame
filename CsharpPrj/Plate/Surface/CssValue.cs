#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UiBackend;

/// <summary>
/// CSS 值拼装单点（单位追加 / style 声明合并）：两载体投影器禁止各写一份。
/// <para>单位less 值 + 载体单位（多值按空白分隔逐个追加）；style 合并保留既有键序、同名覆盖。</para>
/// </summary>
public static class CssValue
{
    /// <summary>单位拼接（单位空 ⇒ 原样）。</summary>
    public static string AppendUnit(string value, string unit)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (string.IsNullOrEmpty(unit)) return value;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++) parts[i] += unit;
        return string.Join(" ", parts);
    }

    /// <summary>DIP/像素 → CSS px 文本（整数值不带小数，否则最多 3 位小数）。</summary>
    public static string Px(float value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 1e-6
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture) + "px"
            : value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    }

    /// <summary>0..1 比例 → CSS 无单位数值（夹取到 0..1，最多 3 位小数）。</summary>
    public static string Ratio(float value)
        => Math.Clamp(value, 0f, 1f).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>角度 → CSS 数字（取整；NaN/∞ 按 0；单位 <c>deg</c> 由调用方补）。</summary>
    public static string Deg(float angle)
        => ((int)Math.Round(float.IsNaN(angle) || float.IsInfinity(angle) ? 0 : angle))
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>零值不写单位（<c>0</c>），非零按 DIP→px。</summary>
    public static string ZeroAware(float value) => Math.Abs(value) < 0.001f ? "0" : Px(value);

    /// <summary>把 <c>prop:value</c> 声明合并进既有 style（同名后者覆盖；不丢弃上游声明）。</summary>
    public static string MergeStyle(string? existing, IEnumerable<string> declarations)
    {
        var order = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void Put(string declaration)
        {
            var at = declaration.IndexOf(':');
            if (at <= 0) return;
            var key = declaration[..at].Trim();
            if (key.Length == 0) return;
            if (!map.ContainsKey(key)) order.Add(key);
            map[key] = declaration[(at + 1)..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (var part in existing.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Put(part);
        }

        foreach (var declaration in declarations) Put(declaration);

        var sb = new StringBuilder();
        foreach (var key in order)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(key).Append(':').Append(map[key]);
        }

        return sb.ToString();
    }
}

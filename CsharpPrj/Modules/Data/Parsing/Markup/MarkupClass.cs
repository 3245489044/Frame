using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Parsing.Markup;

/// <summary>
/// 类名约定（**纯字符串，零 CSS 语义**）—— 消除 <c>cls + "-body"</c> 类前缀派生与空值兜底的重复实现。
/// <para>纪律：不做类名拆分/校验；多类名（<c>"a b"</c>）合法；只做"空/空白 ⇒ 兜底"与"前缀/后缀拼接"。</para>
/// </summary>
public static class MarkupClass
{
    /// <summary>空/空白 ⇒ <paramref name="fallback"/>（与既有 <c>TextUtil.CssClass</c> 同口径）。</summary>
    public static string Fallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;

    /// <summary>前缀派生：<c>{base}{suffix}</c>；基类为空/空白 ⇒ <paramref name="baseFallback"/>。</summary>
    public static string Suffix(string? baseClass, string suffix, string baseFallback = "item")
        => Fallback(baseClass, baseFallback) + (suffix ?? string.Empty);

    /// <summary>多段类名拼接（跳过空/空白段，单空格分隔）。</summary>
    public static string Join(params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part!.Trim());
        }
        return sb.ToString();
    }

    /// <summary>多段类名拼接（跳过空/空白段，按出现顺序去重，单空格分隔）。</summary>
    public static string JoinDistinct(params string?[] parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var trimmed = part!.Trim();
            if (seen.Add(trimmed)) kept.Add(trimmed);
        }
        return string.Join(" ", kept);
    }
}

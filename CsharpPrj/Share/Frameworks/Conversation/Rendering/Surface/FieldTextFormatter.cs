#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 字段值 → 文本的**唯一合成点**（四载体共用；禁各后端各写一份）。
/// <para>未知/缺失 ⇒ 可见占位 <c>?</c>（禁静默省略或默认空串）。</para>
/// </summary>
public static class FieldTextFormatter
{
    /// <summary>字段 → 文本（未知占位走可见标记）。</summary>
    /// <param name="field">字段。</param>
    public static string Format(UiField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.IsUnknown ? "?" : FormatValue(field.Value);
    }

    /// <summary>原始值 → 文本（对象/数组按声明顺序展开）。</summary>
    /// <param name="value">原始值。</param>
    public static string FormatValue(object? value) => value switch
    {
        null => "?",
        string text => text,
        bool flag => flag ? "true" : "false",
        IEnumerable<KeyValuePair<string, string>> pairs => string.Join("; ", pairs.Select(p => $"{p.Key}={p.Value}")),
        System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>()),
        _ => value.ToString() ?? "?",
    };
}
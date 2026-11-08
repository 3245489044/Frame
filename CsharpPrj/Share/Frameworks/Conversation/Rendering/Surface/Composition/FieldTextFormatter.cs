#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 字段值 → 文本的**唯一合成点**（四载体共用；禁各后端各写一份）。
/// <para>未知/缺失 ⇒ 可见占位（<see cref="SurfaceGlyphs"/>；禁裸 <c>?</c>、禁静默省略）。</para>
/// </summary>
public static class FieldTextFormatter
{
    /// <summary>字段 → 文本（未知占位走可见标记）。</summary>
    /// <param name="field">字段。</param>
    public static string Format(UiField field) => Format(field, SurfaceGlyphs.Unknown);

    /// <summary>字段 → 文本（未知占位词可指定）。</summary>
    /// <param name="field">字段。</param>
    /// <param name="unknown">未知占位词（窄载体可传 ASCII 占位）。</param>
    public static string Format(UiField field, string unknown)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.IsUnknown ? unknown : FormatValue(field.Value, unknown);
    }

    /// <summary>原始值 → 文本（对象/数组按声明顺序展开）。</summary>
    /// <param name="value">原始值。</param>
    public static string FormatValue(object? value) => FormatValue(value, SurfaceGlyphs.Unknown);

    /// <summary>原始值 → 文本（未知占位词可指定）。</summary>
    /// <param name="value">原始值。</param>
    /// <param name="unknown">未知占位词（缺省 = 宽载体占位）。</param>
    public static string FormatValue(object? value, string unknown) => value switch
    {
        null => unknown,
        string text => text,
        bool flag => flag ? "true" : "false",
        IEnumerable<KeyValuePair<string, string>> pairs => string.Join("; ", pairs.Select(p => $"{p.Key}={p.Value}")),
        System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>()),
        _ => value.ToString() ?? unknown,
    };
}
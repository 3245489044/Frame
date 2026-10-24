#nullable enable

using System;

namespace Share.Composition.Text;

/// <summary>纯空白串的口径（契约域 = 有值；前置域 = 缺失）。</summary>
public enum BlankSemantics
{
    /// <summary>空白串算已给出（契约域缺省）。</summary>
    Value,

    /// <summary>空白串算未给出（前置门域）。</summary>
    Absent,
}

/// <summary>
/// 原始值 → 四态的<b>唯一映射</b>：键是否存在 × 值形态 × 空白口径。
/// <para>
/// 口径只作参数，不再由调用方各持一个 <c>PresenceSemantics</c> 实例自行裁决；
/// 「null 算不算有值」不再可配 —— 显式 null 一律为 <see cref="TextState.Empty"/>，
/// 是否合法由段的 AllowEmpty 裁决（契约与前置因此对同一输入必然同结论）。
/// </para>
/// </summary>
public static class TextStateMap
{
    /// <summary>键存在性 + 值 + 空白口径 → 段状态。</summary>
    /// <param name="keyExists">键是否存在于事实面。</param>
    /// <param name="value">事实值（可 null）。</param>
    /// <param name="blankAs">纯空白串口径（数值类型不受影响）。</param>
    public static TextState Map(bool keyExists, object? value, BlankSemantics blankAs = BlankSemantics.Value)
    {
        if (!keyExists) return TextState.Unknown;
        if (value is null) return TextState.Empty;
        if (value is string text && string.IsNullOrWhiteSpace(text))
            return blankAs == BlankSemantics.Absent ? TextState.Empty : TextState.Known;
        return TextState.Known;
    }

    /// <summary>值 → 段文本（非文本值取 <c>ToString</c>；null → 空串）。</summary>
    public static string TextOf(object? value)
        => value switch
        {
            null => string.Empty,
            string s => s,
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>
    /// 上下文取值 → 段值（提供者的标准写法：<c>ctx => ctx.BlockOf("l2")</c>）。
    /// 口径只在此处生效，使同一段在契约域/前置域得到同一状态的唯一解释。
    /// </summary>
    public static TextBlock BlockOf(this ISegmentContext context, TextKey key, BlankSemantics blankAs = BlankSemantics.Value)
    {
        ArgumentNullException.ThrowIfNull(context);
        var exists = context.TryGet(key.Value, out var value);
        var state = Map(exists, value, blankAs);
        var text = state == TextState.Known ? TextOf(value) : string.Empty;
        return new TextBlock(
            key, string.Empty, OrderKey.Zero, state, text,
            exists ? null : "missing-key", string.Empty);
    }
}

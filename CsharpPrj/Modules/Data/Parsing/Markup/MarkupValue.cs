using System;
using System.Globalization;

namespace Modules.Parsing.Markup;

/// <summary>
/// 值格式化（**纯值 → 文本，零标记语义**）—— 时长/尺寸/页码各恰一份实现。
/// <para>文案纪律：本层不产任何语言相关文案（页码只出中性的 <c>current/total</c>），
/// 需要中文/多语言时由宿主传入格式串或自行包装。</para>
/// </summary>
public static class MarkupValue
{
    /// <summary>时长（毫秒）→ 紧凑文本：<c>12s</c> / <c>3m05s</c> / <c>1h02m</c>；null / 非正 ⇒ 空串。</summary>
    public static string Duration(long? elapsedMs)
        => elapsedMs is > 0 ? Duration(TimeSpan.FromMilliseconds(elapsedMs.Value)) : string.Empty;

    /// <summary>时长 → 紧凑文本：<c>12s</c> / <c>3m05s</c> / <c>1h02m</c>；负值 ⇒ 按最小值 1 秒。</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h"
                   + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
        if (span.TotalMinutes >= 1)
            return span.Minutes.ToString(CultureInfo.InvariantCulture) + "m"
                   + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
        return Math.Max(1, (int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>字节量 → <c>0.#MB</c> / <c>0.#KB</c> / <c>0B</c>。</summary>
    public static string Size(double bytes)
        => bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + "MB"
            : bytes >= 1024
                ? (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + "KB"
                : bytes.ToString("0", CultureInfo.InvariantCulture) + "B";

    /// <summary>页码 → 中性文本 <c>current/total</c>（不含语言文案；负值按 0）。</summary>
    public static string Page(int current, int total)
        => Math.Max(0, current).ToString(CultureInfo.InvariantCulture)
           + "/" + Math.Max(0, total).ToString(CultureInfo.InvariantCulture);
}

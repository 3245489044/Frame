#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace UiBackend;

/// <summary>
/// 层参数字符串袋（<c>params</c>）的唯一读取口径。
/// <para>
/// 缺省、文化、单位（<c>px</c>）只在这里解释：各载体与解析方一律经本类取原始值，
/// 禁止再各自写 <c>Param(...)</c> / <c>float.TryParse</c> 私有帮助器（否则缺省与容错会再次漂移）。
/// </para>
/// <para>本类零载体字面量、零 I/O、仅 BCL。</para>
/// </summary>
public static class LayerParams
{
    /// <inheritdoc cref="Read(IReadOnlyDictionary{string,string}?, string)"/>
    public static string Read(IReadOnlyDictionary<string, string>? parameters, string name)
    {
        if (parameters is null || string.IsNullOrEmpty(name)) return string.Empty;
        return parameters.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// 解析数值：去空白、容忍 <c>px</c> 后缀、恒用不变文化、拒绝 <c>NaN</c>/<c>Infinity</c>；空串/非法 ⇒ false。
    /// </summary>
    /// <param name="raw">原始文本。</param>
    /// <param name="value">解析结果（失败为 0）。</param>
    public static bool TryFloat(string? raw, out float value)
    {
        value = 0f;
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0) return false;

        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        if (text.Length == 0) return false;

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value)
            && !float.IsInfinity(value);
    }

    /// <summary>取数值；缺省/非法 ⇒ <paramref name="fallback"/>（非法不产生差错，由调用方按需登记）。</summary>
    /// <param name="parameters">参数袋。</param>
    /// <param name="name">键名。</param>
    /// <param name="fallback">缺省值。</param>
    public static float Float(IReadOnlyDictionary<string, string>? parameters, string name, float fallback)
        => TryFloat(Read(parameters, name), out var value) ? value : fallback;

    /// <summary>按 <c>,</c> / <c>;</c> 切分列表（去空白、丢空项）。</summary>
    /// <param name="raw">原始文本。</param>
    public static string[] SplitList(string? raw)
        => Clean(raw).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>按空白切分（用于单值 + 位置等组合项）。</summary>
    /// <param name="raw">原始文本。</param>
    public static string[] SplitTokens(string? raw)
        => Clean(raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>夹到 <c>[0,1]</c>。</summary>
    /// <param name="value">原始值。</param>
    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    /// <summary>夹到非负。</summary>
    /// <param name="value">原始值。</param>
    public static float NonNegative(float value) => value < 0f ? 0f : value;

    private static string Clean(string? raw) => raw?.Trim() ?? string.Empty;
}

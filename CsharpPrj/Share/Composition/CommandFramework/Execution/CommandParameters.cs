using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CommandFramework.Execution.Commands;

/// <summary>
/// 命令参数读取器（Share A-1 单源）：统一 bool/int/long/string 的缺省值、存在性与数值解析语义，
/// 消除各命令系统重复的 <c>ParseBool/ParseTimeout/GetParamString</c>。
/// <para>
/// 语义约定（与既有命令面保持一致）：
/// <list type="bullet">
/// <item>缺失、值为 null、空白字符串 → 视为「未提供」，返回缺省值。</summary>
/// <item>bool 接受 true/1（忽略大小写），其余均 false。</item>
/// <item>int/long 使用 InvariantCulture；非法值 → 未提供（返回缺省），不抛异常。</item>
/// <item>string 直接 ToString()（保留原始数值形态）。</item>
/// </list>
/// </para>
/// 只读、无状态、线程安全；参数的「业务含义」仍由各命令系统解释。
/// </summary>
public static class CommandParameters
{
    public static bool Contains(IReadOnlyDictionary<string, object?>? parameters, string key)
        => TryGetRaw(parameters, key, out _);

    /// <summary>读取 bool；缺失/非法 → <paramref name="defaultValue"/>。</param>
    public static bool GetBool(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        bool defaultValue = false)
    {
        if (!TryGetRaw(parameters, key, out var raw))
            return defaultValue;

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;

        return text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
               || text.Trim() == "1";
    }

    /// <summary>
    /// 读取 int：返回 (是否存在且可解析, 值)。存在但非法 → (false, 0)（调用方可据此回退缺省）。
    /// 用于「显式 timeout=0 表示无限」这类需要区分「未提供」与「提供 0」的场景。
    /// </summary>
    public static (bool Present, int Value) TryGetInt(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        if (!TryGetRaw(parameters, key, out var raw))
            return (false, 0);

        if (raw is int intValue)
            return (true, intValue);

        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? (true, parsed)
            : (false, 0);
    }

    /// <summary>读取 int；缺失/非法 → <paramref name="defaultValue"/>。</summary>
    public static int GetInt(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        int defaultValue)
        => TryGetInt(parameters, key) is { Present: true } result ? result.Value : defaultValue;

    /// <summary>读取 long；缺失/非法 → null。</summary>
    public static long? GetLong(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        if (!TryGetRaw(parameters, key, out var raw))
            return null;

        if (raw is long longValue)
            return longValue;

        return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>读取字符串；缺失/空白 → null。</summary>
    public static string? GetString(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        if (!TryGetRaw(parameters, key, out var raw))
            return null;

        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>读取字符串；缺失/空白 → <paramref name="defaultValue"/>。</summary>
    public static string GetString(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        string defaultValue)
        => GetString(parameters, key) ?? defaultValue;

    /// <summary>
    /// 读取词表（failWhen/failContains 等）：按逗号/竖线/空格切分，去重（忽略大小写）。
    /// 缺失 → 空数组（永不为 null，便于调用方直接 Any/Contains）。
    /// </summary>
    public static IReadOnlyList<string> GetTokens(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        var text = GetString(parameters, key);
        if (text is null)
            return Array.Empty<string>();

        return text
            .Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetRaw(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        out object raw)
    {
        raw = null!;
        return parameters is not null
               && parameters.TryGetValue(key, out var value)
               && value is not null
               && !string.IsNullOrWhiteSpace(value.ToString());
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Modules.Parsing.Invocation;

/// <summary>一次整数参数读取的三态结果（Present=键存在且非空；Valid=可解析）。</summary>
public readonly record struct IntArg(bool Present, bool Valid, int Value, string? Raw);

/// <summary>
/// 命令参数读取（唯一实现，供内核与命令层共用）：缺省值/存在性/数值解析语义只在这里定义。
/// 与 <c>CommandFramework.CommandParameters</c> 的差别：本类把「存在但非法」显式暴露（Present=true, Valid=false），
/// 调用方不得把它静默降级成缺省值。
/// </summary>
public static class InvocationArgs
{
    public const string TimeoutKey = "timeout";

    /// <summary>原始文本；键缺失/空白 ⇒ null。</summary>
    public static string? Raw(IReadOnlyDictionary<string, object?>? args, string key)
    {
        if (args is null || string.IsNullOrWhiteSpace(key)) return null;
        if (!args.TryGetValue(key, out var raw) || raw is null) return null;
        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>整数读取（三态；非法不抛）。</summary>
    public static IntArg Int(IReadOnlyDictionary<string, object?>? args, string key)
    {
        if (args is null || string.IsNullOrWhiteSpace(key)) return new IntArg(false, false, 0, null);
        if (!args.TryGetValue(key, out var raw) || raw is null) return new IntArg(false, false, 0, null);

        switch (raw)
        {
            case int i: return new IntArg(true, true, i, i.ToString(CultureInfo.InvariantCulture));
            case long l when l is >= int.MinValue and <= int.MaxValue: return new IntArg(true, true, (int)l, l.ToString(CultureInfo.InvariantCulture));
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text)) return new IntArg(false, false, 0, null);
        text = text.Trim();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? new IntArg(true, true, parsed, text)
            : new IntArg(true, false, 0, text);
    }

    /// <summary>
    /// 命令级预算（毫秒）唯一读取：缺省 ⇒ <paramref name="fallbackMs"/>；非法/负值 ⇒ 错误文本（不静默）。
    /// 显式 0 = 未声明预算（直通执行，与既有语义一致）。
    /// </summary>
    public static (int? BudgetMs, string? Error) Timeout(
        IReadOnlyDictionary<string, object?>? args,
        int? fallbackMs = null,
        string key = TimeoutKey)
    {
        var read = Int(args, key);
        if (!read.Present) return (fallbackMs, null);
        if (!read.Valid) return (null, $"{key} 不是整数：'{read.Raw}'");
        if (read.Value < 0) return (null, $"{key} 不得为负：'{read.Raw}'");
        return read.Value == 0 ? (null, null) : (read.Value, null);
    }

    /// <summary>long 预算形态（执行预算策略消费）。</summary>
    public static long? TimeoutBudgetMs(
        IReadOnlyDictionary<string, object?>? args,
        string key = TimeoutKey)
    {
        var (budget, error) = Timeout(args, fallbackMs: null, key);
        return error is null ? budget : null;
    }

    /// <summary>字符串读取；缺失/空白 ⇒ null。</summary>
    public static string? Text(IReadOnlyDictionary<string, object?>? args, string key) => Raw(args, key);

    /// <summary>bool 读取（true/1；其余 false；缺失 ⇒ fallback）。</summary>
    public static bool Bool(IReadOnlyDictionary<string, object?>? args, string key, bool fallback = false)
    {
        var text = Raw(args, key);
        if (text is null) return fallback;
        return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    /// <summary>词表读取（逗号/竖线/空格分隔；去重；缺失 ⇒ 空数组）。</summary>
    public static IReadOnlyList<string> Tokens(IReadOnlyDictionary<string, object?>? args, string key)
    {
        var text = Raw(args, key);
        if (text is null) return Array.Empty<string>();
        return text.Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

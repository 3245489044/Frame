using System.Globalization;

namespace Plate.DslMethod.Parsing;

/// <summary>
/// 通用字符串值解析工具：把 DSL 声明字段的原始字符串解析为强类型值。
/// 仅含与域语义无关的通用字符串处理；域映射（名→轴/层级、[CR] 键拼装）不落本类。
/// </summary>
public static class StringValueParser
{
    /// <summary>空/空白 → null，否则去首尾空白。</summary>
    public static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    /// <summary>解析整数字段值；解析失败回退默认值。</summary>
    public static int IntValue(string? s, int def)
        => int.TryParse(s, out var v) ? v : def;

    /// <summary>解析双精度字段值（InvariantCulture）；解析失败回退默认值。</summary>
    public static double DoubleValue(string? s, double def)
        => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : def;

    /// <summary>按分隔符拆分列表字段（去除空项与首尾空白）。</summary>
    public static List<string> SplitList(string? s, char sep)
        => string.IsNullOrWhiteSpace(s)
            ? new List<string>()
            : s.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>解析上限字段：空 / unbounded / * → 无界；否则解析为数值上限（解析失败 → null 且非无界）。</summary>
    public static (double? Max, bool Unbounded) ParseMax(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) ||
            string.Equals(s, "unbounded", StringComparison.OrdinalIgnoreCase) ||
            s == "*")
            return (null, true);

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? (v, false)
            : (null, false);
    }

    /// <summary>任意通配判定：空 / any / *。</summary>
    public static bool IsAny(string? s)
        => string.IsNullOrWhiteSpace(s) ||
        string.Equals(s, "any", StringComparison.OrdinalIgnoreCase) ||
        s == "*";

    /// <summary>解析 dep 字段：空 → 未废弃；含 <c>replaced-by(X)</c> → 废弃且指向 X；其它非空 → 废弃无替换。</summary>
    public static (bool Deprecated, string? ReplacedBy) ParseDep(string dep)
    {
        if (string.IsNullOrWhiteSpace(dep)) return (false, null);

        const string marker = "replaced-by(";
        var i = dep.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var start = i + marker.Length;
            var close = dep.IndexOf(')', start);
            var target = close > start ? dep[start..close].Trim() : "";
            return (true, string.IsNullOrEmpty(target) ? null : target);
        }
        return (true, null);
    }

    /// <summary>清洗为合法 C# 标识符（非法字符替换为下划线，首字符须字母/下划线）。</summary>
    public static string SanitizeIdentifier(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var s = new string(chars);
        if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_')) s = "_" + s;
        return s;
    }
}
using System;
using System.Globalization;
using System.Linq;

namespace FrameworkAbstractions.Declarative;

/// <summary>
/// 严格取值（声明面专用）：未定义枚举值 / 非 true|false 文本一律拒绝，禁「未知默认成功」。
/// </summary>
public static class StrictParse
{
    /// <summary>严格布尔：仅 <c>true</c>/<c>false</c>（忽略大小写）；其它（含 null/空/1/yes）抛。</summary>
    public static bool Bool(string? raw, string context)
    {
        if (TryBool(raw, out var value)) return value;
        throw new InvalidOperationException($"decl.invalid-bool: {context} 取值 '{raw}' 非 true|false。");
    }

    /// <summary>严格布尔（不抛）：仅 <c>true</c>/<c>false</c> 成功。</summary>
    public static bool TryBool(string? raw, out bool value)
    {
        value = false;
        var text = raw?.Trim();
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        return string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>严格枚举：必须是已定义名称（拒绝数值与未定义名），否则抛。</summary>
    public static TEnum DefinedEnum<TEnum>(string? raw, string context) where TEnum : struct, Enum
    {
        if (TryDefinedEnum<TEnum>(raw, out var value)) return value;
        throw new InvalidOperationException(
            $"decl.undefined-enum: {context} 取值 '{raw}' 不在 {typeof(TEnum).Name} 定义域 [{string.Join(", ", Enum.GetNames<TEnum>())}]。");
    }

    /// <summary>严格枚举（不抛）：数值形态与未定义名均失败。</summary>
    public static bool TryDefinedEnum<TEnum>(string? raw, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text.All(char.IsDigit)) return false;
        return Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>严格整数（不变文化；失败即抛）。</summary>
    public static int Int32(string? raw, string context)
        => int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"decl.invalid-int: {context} 取值 '{raw}' 非整数。");

    /// <summary>严格正整数（0/负值即抛）。</summary>
    public static int PositiveInt32(string? raw, string context)
    {
        var value = Int32(raw, context);
        return value > 0 ? value : throw new InvalidOperationException($"decl.invalid-int: {context} 取值 '{raw}' 非正数。");
    }
}

namespace Plate.JsonMethod.Utilities;

/// <summary>通用枚举解析（大小写不敏感；空/非法 → null），统一承载宿主里散落的 Enum.TryParse。</summary>
public static class EnumConvert
{
    /// <summary>解析字符串为枚举；空/非法 → null。不抛异常。</summary>
    public static TEnum? TryParse<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<TEnum>(value!, ignoreCase: true, out var result) ? result : null;
    }
}
namespace Plate.JsonMethod.Utilities;

/// <summary>
/// 通用 JSON 标量值转换（数值/字符串 → 目标类型）；空/非法安全回退，不抛异常。
/// 统一承载散落在各宿主里的 int.TryParse / ToStr 等值转换，避免重复实现。
/// </summary>
public static class JsonValueConverter
{
    /// <summary>对象 → 可空 int（null/非法 → null）。</summary>
    public static int? ToNullableInt(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            double d when double.IsFinite(d) => (int)d,
            decimal m when m is >= int.MinValue and <= int.MaxValue => (int)m,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    /// <summary>对象 → int（null/非法 → <paramref name="fallback"/>）。</summary>
    public static int ToInt(object? value, int fallback = 0)
        => ToNullableInt(value) ?? fallback;

    /// <summary>对象 → 可空 double（null/非法 → null）。</summary>
    public static double? ToNullableDouble(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            int i => i,
            long l => l,
            double d when double.IsFinite(d) => d,
            decimal m => (double)m,
            _ => double.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    /// <summary>对象 → 规范化字符串（null → null）。</summary>
    public static string? AsString(object? value)
        => value?.ToString();
}
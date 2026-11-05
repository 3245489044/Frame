using System;
using System.Text.Json;

namespace Modules.Regulation;

/// <summary>
/// 标量数值归一器（纯函数）：把跨形态存储的数值（int/long/short/byte/double/float/decimal/JsonElement/字符串）
/// 统一归为 int 并夹紧到下限。状态经 JSON 往返后可能非 int，故需宽容归一，且保证不低 min。
/// 溢出按 Int32 边界饱和（不回绕、不静默落入 min）；NaN/无穷按无值处理。
/// </summary>
public static class ScalarCoercer
{
    public static int ToInt(object? value, int min = 1)
    {
        var v = value switch
        {
            int i => i,
            long l => Saturate(l),
            short s => s,
            byte b => b,
            double d => Saturate(d),
            float f => Saturate((double)f),
            decimal m => Saturate(m),
            // JsonElement 经 JSON 往返可能非 Int32（大整数/小数/字符串），逐形态宽容归一，避免静默落入 min。
            JsonElement je when je.TryGetInt32(out var i) => i,
            JsonElement je when je.TryGetInt64(out var l) => Saturate(l),
            JsonElement je when je.TryGetDouble(out var d) => Saturate(d),
            JsonElement je when je.TryGetDecimal(out var m) => Saturate(m),
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var i) => i,
            string s when int.TryParse(s, out var i) => i,
            _ => min,
        };
        return Math.Max(min, v);
    }

    private static int Saturate(long value)
        => value >= int.MaxValue ? int.MaxValue
            : value <= int.MinValue ? int.MinValue
            : (int)value;

    private static int Saturate(decimal value)
        => value >= int.MaxValue ? int.MaxValue
            : value <= int.MinValue ? int.MinValue
            : (int)value;

    private static int Saturate(double value)
        => double.IsNaN(value) ? int.MinValue
            : value >= int.MaxValue ? int.MaxValue
            : value <= int.MinValue ? int.MinValue
            : (int)value;
}

using System;
using System.Text.Json;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 标量数值归一器（纯函数）：把跨形态存储的数值（int/long/short/byte/double/float/decimal/JsonElement/字符串）
/// 统一归为 int 并夹紧到下限。状态经 JSON 往返后可能非 int，故需宽容归一，且保证不低 min。
/// </summary>
public static class ScalarCoercer
{
    public static int ToInt(object? value, int min = 1)
    {
        var v = value switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            double d => (int)d,
            float f => (int)f,
            decimal m => (int)m,
            // JsonElement 经 JSON 往返可能非 Int32（大整数/小数/字符串），逐形态宽容归一，避免静默落入 min。
            JsonElement je when je.TryGetInt32(out var i) => i,
            JsonElement je when je.TryGetInt64(out var l) => (int)l,
            JsonElement je when je.TryGetDouble(out var d) => (int)d,
            JsonElement je when je.TryGetDecimal(out var m) => (int)m,
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var i) => i,
            string s when int.TryParse(s, out var i) => i,
            _ => min,
        };
        return Math.Max(min, v);
    }
}

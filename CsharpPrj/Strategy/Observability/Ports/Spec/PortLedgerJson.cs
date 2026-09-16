namespace Strategy.Observability.Ports;

using System.Text.Json;

/// <summary>
/// 台账 JSON 读取原语（R2-2）：字符串 / 整数 / 布尔 / 字符串数组 / 元素数组的宽容读取（缺失或类型不符 → null）。
/// <para>由 <c>PortLedger</c> 与 <c>PortLedgerScriptedReader</c> 共用；读取口径与迁移前逐字一致（含诊断文案）。</para>
/// </summary>
internal static class PortLedgerJson
{
    /// <summary>字符串数组字段读取（缺失 → null）。</summary>
    internal static IReadOnlyList<string>? StrList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    /// <summary>JSON 元素数组字段读取（缺失 → null）。</summary>
    internal static IReadOnlyList<JsonElement>? Elements(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray().Select(item => item.Clone()).ToArray();
    }
    internal static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    internal static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool? Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}

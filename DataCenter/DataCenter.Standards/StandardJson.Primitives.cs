using System.Text.Json;

namespace DataCenter.Standards;

/// <summary>
/// 台账 JSON 读取原语（B3 自内核 <c>PortLedgerJson</c> 逐字迁移）：字符串 / 整数 / 布尔 / 字符串数组 /
/// 元素数组的宽容读取（缺失或类型不符 → null）。
/// <para>
/// 辨异：本组原语供 <c>impl</c> 段读取使用，语义与内核**逐字一致**（故与既有 <c>Text</c>/<c>Number</c>/<c>Strings</c>
/// 并存：后者的 <c>Strings</c> 接受裸字符串并包装为单元素数组，而 <c>StrList</c> 不接受 —— 迁移期不得互相替换）。
/// </para>
/// </summary>
public static partial class StandardJson
{
    /// <summary>字符串数组字段读取（缺失或非数组 → null）。</summary>
    private static IReadOnlyList<string>? StrList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    /// <summary>JSON 元素数组字段读取（缺失或非数组 → null）。</summary>
    private static IReadOnlyList<JsonElement>? Elements(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>整数读取（缺失或非整数 → null）。</summary>
    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>字符串读取（缺失或非字符串 → null）。</summary>
    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>布尔读取（缺失或非布尔 → null）。</summary>
    private static bool? Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}

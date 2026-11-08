using System.Text.Json.Nodes;

namespace Modules.Node.Frame;

/// <summary>
/// 帧字段袋**三态解析单源**（N-02）：字段缺失/不可判定 ⇒ <c>null</c>（未知），禁默认 false 冒充事实。
/// <para>唯一实现：JSON 节点（<see cref="TryReadBool"/>）与文本字段（<see cref="ParseFlag"/>）共用同一布尔口径。</para>
/// </summary>
public static class FrameFieldBag
{
    /// <summary>
    /// JSON 字段 → 三态布尔（JSON 布尔 / "true"/"false" / "1"/"0"；其它 ⇒ <c>null</c>）。
    /// </summary>
    /// <param name="node">字段节点（可空）。</param>
    public static bool? TryReadBool(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<bool>(out var flag)) return flag;
        return value.TryGetValue<string>(out var text) ? ParseFlag(text) : null;
    }

    /// <summary>文本字段 → 三态布尔（<c>true/false</c>、<c>1/0</c>；缺失/不可解析 ⇒ <c>null</c>）。</summary>
    /// <param name="text">字段文本（可空）。</param>
    public static bool? ParseFlag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (bool.TryParse(text, out var parsed)) return parsed;

        return text.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };
    }
}

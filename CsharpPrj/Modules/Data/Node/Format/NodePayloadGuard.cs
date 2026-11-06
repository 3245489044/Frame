using System.Text.Json;

namespace Modules.Node.Format;

/// <summary>
/// 业务节点载荷形态闸门（通用）：判定 JSON 是否为「业务节点载荷」（对象且含业务标记键）。
/// <para>
/// 写回前的形态闸门：非业务载荷（通用 JSON / 数组 / 标量）不得经业务载荷编解码往返，
/// 否则原内容会被替换为空业务载荷。本类只做形态判定，不做编解码、不绑定 DTO。
/// </para>
/// </summary>
public static class NodePayloadGuard
{
    /// <summary>业务节点载荷标记键（业务载荷 = 含此键的对象）。</summary>
    public const string BusinessMarker = "Outputs";

    /// <summary>是否为业务节点载荷（对象且含 <see cref="BusinessMarker"/>）；空/非 JSON ⇒ false。</summary>
    public static bool IsBusinessPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(BusinessMarker, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>是否为 JSON 对象（不要求业务标记）；空/非 JSON ⇒ false。</summary>
    public static bool IsJsonObject(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

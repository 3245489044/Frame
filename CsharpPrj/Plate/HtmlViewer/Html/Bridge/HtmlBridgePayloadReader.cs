#nullable enable

using System.Text.Json;
using Plate.JsonMethod.Utilities;

namespace UiBackend.Html;

/// <summary>
/// htmlviewer 消息桥负载解析（通用，零领域）——把 <c>postMessage</c> JSON 归一为
/// <see cref="HtmlBridgePayload"/>（<b>不预设任何领域字段</b>）。复用 <see cref="JsonTolerantReader"/> 元素原语：
/// 注释 / 尾随逗号容忍；非法 / 空 / 非对象 ⇒ <see cref="HtmlBridgePayload.Empty"/>（不抛）。
/// </summary>
public static class HtmlBridgePayloadReader
{
    /// <summary>解析负载 → <see cref="HtmlBridgePayload"/>（异常 / 缺失 / 非对象 ⇒ 空负载）。</summary>
    public static HtmlBridgePayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return HtmlBridgePayload.Empty;
        try
        {
            return JsonTolerantReader.ParseRoot(payload!, static root =>
                root.ValueKind == JsonValueKind.Object ? new HtmlBridgePayload(root) : HtmlBridgePayload.Empty);
        }
        catch (JsonException)
        {
            return HtmlBridgePayload.Empty;
        }
    }
}

using System;
using System.Text.Json.Nodes;

namespace Modules.Node.Stream;

/// <summary>
/// 帧载荷**事实字段**写入（通用；域无关）：按「匹配字段 = 匹配值」在帧上补写事实字段，
/// 供写侧把「终答 / 已归档」等**事实**落进载荷（读侧据字段选择，禁位置推断）。
/// <para>唯一实现：字段名与匹配条件一律由调用方给出，本件不含任何领域词。</para>
/// </summary>
public static class StreamPayloadFacts
{
    /// <summary>
    /// 在 <c>rounds[].frames[]</c> 上，对 <paramref name="matchField"/> 等于
    /// <paramref name="matchValue"/> 的帧补写 <paramref name="field"/> =
    /// <paramref name="value"/>；无命中/不可解析 ⇒ 原样返回（零改写）。
    /// </summary>
    /// <param name="payloadJson">聚合载荷 JSON。</param>
    /// <param name="field">要写入的事实字段名。</param>
    /// <param name="value">事实值。</param>
    /// <param name="matchField">匹配字段名。</param>
    /// <param name="matchValue">匹配字段期望值。</param>
    public static string WithFlagOnMatch(
        string payloadJson, string field, bool value, string matchField, bool matchValue)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) ||
            string.IsNullOrWhiteSpace(field) ||
            string.IsNullOrWhiteSpace(matchField))
        {
            return payloadJson;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(payloadJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return payloadJson;
        }

        if (parsed is not JsonObject root || root["rounds"] is not JsonArray rounds)
        {
            return payloadJson;
        }

        var changed = false;
        foreach (var round in rounds)
        {
            if (round is not JsonObject roundObject || roundObject["frames"] is not JsonArray frames)
            {
                continue;
            }

            foreach (var node in frames)
            {
                if (node is not JsonObject frame) continue;
                if (!TryReadBool(frame[matchField], out var matched) || matched != matchValue) continue;
                if (TryReadBool(frame[field], out var existing) && existing == value) continue;

                frame[field] = JsonValue.Create(value);
                changed = true;
            }
        }

        return changed ? root.ToJsonString() : payloadJson;
    }

    /// <summary>读帧字段布尔值（JSON 布尔或 "true"/"false" 文本；不可判定 ⇒ false + false）。</summary>
    /// <param name="node">字段节点。</param>
    /// <param name="value">解析结果。</param>
    public static bool TryReadBool(JsonNode? node, out bool value)
    {
        value = false;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<bool>(out var flag))
        {
            value = flag;
            return true;
        }

        return jsonValue.TryGetValue<string>(out var text) && bool.TryParse(text, out value);
    }
}

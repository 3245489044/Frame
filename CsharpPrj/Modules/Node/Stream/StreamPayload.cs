using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Modules.Node.Stream;

/// <summary>
/// 一条**帧级读记录**（通用；域无关）：<see cref="Round"/> 来自外层轮分组、<see cref="Seq"/> 来自帧载荷，
/// <see cref="Fields"/> = 帧对象逐字段展平（含 kind/seq/atUtc 与其余载荷字段）。
/// </summary>
/// <param name="Round">所属轮次（载荷 <c>rounds[].round</c>）。</param>
/// <param name="Seq">到达序（帧载荷 <c>seq</c>；容器级、跨轮不重置）。</param>
/// <param name="Kind">帧类别（帧载荷 <c>kind</c>；缺失 ⇒ 空串）。</param>
/// <param name="Fields">帧对象展平字段袋（塑形/选择共用）。</param>
public sealed record StreamFrameRecord(
    int Round,
    long Seq,
    string Kind,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>单行 Stream 载荷的解析产物（通用；域无关）：分区 → 轮 → 帧 展平为帧记录序列。</summary>
/// <param name="PartitionKey">载荷自述分区键（须与容器一致，否则不采信）。</param>
/// <param name="StartedAtUtc">首次成轮时刻（可空）。</param>
/// <param name="EndedAtUtc">最后一帧到达时刻（可空）。</param>
/// <param name="Frames">帧记录序列（保序）。</param>
public sealed record StreamPayload(
    string PartitionKey,
    string? StartedAtUtc,
    string? EndedAtUtc,
    IReadOnlyList<StreamFrameRecord> Frames);

/// <summary>
/// Stream 载荷**字段面差异参数**（声明驱动；全部经构造注入，禁在读取逻辑内硬编码领域常量）：
/// 分区键字段名 / 轮-帧字段名 / 到达序字段名 / 类别字段名 / 时间字段名 + 可见化前缀标签。
/// </summary>
/// <param name="Label">可见化原因串前缀（域可辨；不参与解析语义）。</param>
/// <param name="PartitionKeyField">分区键字段名（默认 <c>taskKey</c>）。</param>
/// <param name="RoundField">轮号字段名（默认 <c>round</c>）。</param>
/// <param name="FramesField">帧数组字段名（默认 <c>frames</c>）。</param>
/// <param name="RoundsField">轮分组字段名（默认 <c>rounds</c>）。</param>
/// <param name="KindField">类别字段名（默认 <c>kind</c>）。</param>
/// <param name="SeqField">到达序字段名（默认 <c>seq</c>）。</param>
/// <param name="AtUtcField">帧时刻字段名（默认 <c>atUtc</c>）。</param>
/// <param name="StartedAtUtcField">开始时刻字段名（默认 <c>startedAtUtc</c>）。</param>
/// <param name="EndedAtUtcField">结束时刻字段名（默认 <c>endedAtUtc</c>）。</param>
public sealed record StreamPayloadSchema(
    string Label,
    string PartitionKeyField,
    string RoundField,
    string FramesField,
    string RoundsField,
    string KindField,
    string SeqField,
    string AtUtcField,
    string StartedAtUtcField,
    string EndedAtUtcField)
{
    /// <summary>现行累积器序列化口径（轮嵌套；分区键 <c>taskKey</c>）。</summary>
    public static StreamPayloadSchema Default { get; } = new(
        "stream", "taskKey", "round", "frames", "rounds", "kind", "seq", "atUtc", "startedAtUtc", "endedAtUtc");
}

/// <summary>
/// 单行 Stream 载荷的**唯一解析入口**（通用；一实现服务多域，禁各域各写一套）：
/// 解析 <c>{taskKey,startedAtUtc,endedAtUtc,rounds:[{round,frames:[{seq,kind,atUtc,...}]}]}</c> →
/// 帧记录序列，并校验分区键与容器一致（不一致 ⇒ 不采信 + 可见化原因）。
/// <para>失败/不一致一律返回原因串（结构事实，非叙述），不抛、不静默放行。</para>
/// </summary>
public static class StreamPayloadReader
{
    /// <summary>
    /// 解析单行载荷。返回 <c>false</c> ⇒ 不采信（原因见 <paramref name="reason"/>）。
    /// 无轮/无帧属**合法空**：返回 <c>true</c> 且 <see cref="StreamPayload.Frames"/> 为空。
    /// </summary>
    /// <param name="payloadJson">单行载荷 JSON（可空）。</param>
    /// <param name="container">容器（分区键；须与载荷自述一致）。</param>
    /// <param name="schema">字段面差异参数。</param>
    /// <param name="payload">解析产物（失败为 null）。</param>
    /// <param name="reason">不采信原因（成功为 null）。</param>
    public static bool TryRead(
        string? payloadJson,
        string container,
        StreamPayloadSchema schema,
        out StreamPayload? payload,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(schema);
        payload = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            reason = $"{schema.Label}.payload-missing：单行节点无载荷（不伪造帧）";
            return false;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(payloadJson!);
        }
        catch (System.Text.Json.JsonException ex)
        {
            reason = $"{schema.Label}.payload-unparsable：{ex.Message}";
            return false;
        }

        if (parsed is not JsonObject root)
        {
            reason = $"{schema.Label}.payload-not-object：载荷非对象（不采信）";
            return false;
        }

        var partition = ReadText(root[schema.PartitionKeyField]) ?? string.Empty;
        if (!string.Equals(partition, container, StringComparison.Ordinal))
        {
            reason = $"{schema.Label}.{schema.PartitionKeyField}-mismatch：载荷 {schema.PartitionKeyField}='{partition}' ≠ 容器 '{container}'（不采信）";
            return false;
        }

        var frames = new List<StreamFrameRecord>();
        if (root[schema.RoundsField] is JsonArray rounds)
        {
            foreach (var roundNode in rounds)
            {
                if (roundNode is not JsonObject roundObject)
                {
                    continue;
                }

                var round = ReadInt(roundObject[schema.RoundField]) ?? 0;
                if (roundObject[schema.FramesField] is not JsonArray frameArray)
                {
                    continue;
                }

                foreach (var frameNode in frameArray)
                {
                    if (frameNode is not JsonObject frameObject)
                    {
                        continue;
                    }

                    var fields = Flatten(frameObject);
                    var kind = fields.TryGetValue(schema.KindField, out var k) ? k : string.Empty;
                    var seq = fields.TryGetValue(schema.SeqField, out var s) &&
                              long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeq)
                        ? parsedSeq
                        : 0L;
                    frames.Add(new StreamFrameRecord(round, seq, kind, fields));
                }
            }
        }

        payload = new StreamPayload(
            partition,
            ReadText(root[schema.StartedAtUtcField]),
            ReadText(root[schema.EndedAtUtcField]),
            frames);
        return true;
    }

    /// <summary>帧对象逐字段展平为字符串袋（null 值跳过；字符串直取，其余紧凑 JSON）。</summary>
    private static IReadOnlyDictionary<string, string> Flatten(JsonObject frame)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in frame)
        {
            if (pair.Value is null)
            {
                continue;
            }

            fields[pair.Key] = pair.Value is JsonValue value && value.TryGetValue<string>(out var text)
                ? text ?? string.Empty
                : pair.Value.ToJsonString();
        }

        return fields;
    }

    private static string? ReadText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return null;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return null;
    }
}

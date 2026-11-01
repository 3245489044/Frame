using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Modules.Node.Frame;

/// <summary>
/// 合并产物（通用；域无关）：载荷 JSON + 合并账（既有/本轮/结果帧数、覆盖数、身份不可识别数、是否整载荷替换）。
/// <para>账目供调用方可见化（不静默丢帧）；替换策略触发时 <see cref="ReplacedWholePayload"/> = true。</para>
/// </summary>
/// <param name="PayloadJson">合并后的单行载荷 JSON。</param>
/// <param name="ExistingFrames">既有载荷帧数。</param>
/// <param name="IncomingFrames">本次载荷帧数。</param>
/// <param name="MergedFrames">结果帧数。</param>
/// <param name="ReplacedFrames">被本轮同身份帧覆盖的条数。</param>
/// <param name="UnidentifiableFrames">身份不可识别（回落文本键）的条数。</param>
/// <param name="ReplacedWholePayload">是否因 run 变更整载荷替换（单 run 语义）。</param>
public sealed record FramePayloadMergeResult(
    string PayloadJson,
    int ExistingFrames,
    int IncomingFrames,
    int MergedFrames,
    int ReplacedFrames,
    int UnidentifiableFrames,
    bool ReplacedWholePayload);

/// <summary>
/// Stream 单行载荷的**跨轮合并**（通用；域无关）：把本次 run 的聚合载荷与既有已落库单行载荷
/// **按轮合并**（本轮覆盖同轮），使「一个容器恰一行 + 载荷内 <c>rounds[]→frames[]</c>」在多次 run
/// 之间**累积**（否则每轮 upsert 会以「仅本轮帧」覆盖旧载荷 ⇒ 历史轮次丢失）。
/// <para>
/// 身份口径（SingleImplementation）：去重键 = <see cref="FrameIdentity.Key"/>（run + 轮 + 到达序），
/// **禁**以整帧 JSON 文本当键（<c>atUtc</c> 每次提交不同 ⇒ 去重恒不命中 ⇒ 同 seq 反复并集）。
/// 同键语义 = **新到覆盖旧**（本轮覆盖同轮）；身份不可读 ⇒ 回落文本键并计入账目（不静默丢）。
/// </para>
/// <para>分区键不一致 ⇒ 不合并（返回新载荷，不把他容器数据并入）。</para>
/// </summary>
public static class FramePayloadMerge
{
    /// <summary>合并既有载荷与本次载荷（按轮去重覆盖；返回合并后的 JSON）。</summary>
    /// <param name="existingJson">既有单行载荷 JSON（可空）。</param>
    /// <param name="incomingJson">本次 run 聚合载荷 JSON。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    public static string Merge(string? existingJson, string incomingJson, FramePayloadSchema schema)
        => MergeResult(existingJson, incomingJson, schema).PayloadJson;

    /// <summary>合并并返回账目（身份去重 + 覆盖计数 + run 变更替换）。</summary>
    /// <param name="existingJson">既有单行载荷 JSON（可空）。</param>
    /// <param name="incomingJson">本次 run 聚合载荷 JSON。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    public static FramePayloadMergeResult MergeResult(
        string? existingJson, string incomingJson, FramePayloadSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            var incomingOnly = CountFrames(incomingJson, schema);
            return new FramePayloadMergeResult(incomingJson, 0, incomingOnly, incomingOnly, 0, 0, false);
        }

        JsonObject? existing;
        JsonObject? incoming;
        try
        {
            existing = JsonNode.Parse(existingJson) as JsonObject;
            incoming = JsonNode.Parse(incomingJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return new FramePayloadMergeResult(incomingJson, 0, 0, 0, 0, 0, false);
        }

        if (existing is null || incoming is null)
        {
            return new FramePayloadMergeResult(incomingJson, 0, 0, 0, 0, 0, false);
        }

        var existingFrames = CountFrames(existing, schema);
        var incomingFrames = CountFrames(incoming, schema);

        var incomingKey = incoming[schema.PartitionKeyField]?.ToString();
        var existingKey = existing[schema.PartitionKeyField]?.ToString();
        if (!string.Equals(incomingKey, existingKey, StringComparison.Ordinal))
        {
            // 分区键不一致：不并入他容器数据（防串味）。
            return new FramePayloadMergeResult(
                incomingJson, existingFrames, incomingFrames, incomingFrames, 0, 0, false);
        }

        // 跨 run 驻留策略（声明面/构造注入）：单 run 语义 ⇒ 旧 run 帧不再并集（治「同 seq 跨 run 累积」）。
        var existingRun = existing[schema.RunField]?.ToString();
        var incomingRun = incoming[schema.RunField]?.ToString();
        if (schema.ReplaceOnRunChange
            && incomingRun is not null
            && !string.Equals(existingRun, incomingRun, StringComparison.Ordinal))
        {
            return new FramePayloadMergeResult(
                incomingJson, existingFrames, incomingFrames, incomingFrames, 0, 0, true);
        }

        var buckets = new SortedDictionary<int, FrameBucket>(Comparer<int>.Default);
        var replaced = 0;
        var unidentifiable = 0;
        Collect(existing, schema, buckets, replace: false, ref replaced, ref unidentifiable);
        Collect(incoming, schema, buckets, replace: true, ref replaced, ref unidentifiable);

        // 以**文本**重建轮数组，避免复用已挂父节点的 JsonNode（JsonNode 单父约束）。
        var roundsJson = new StringBuilder("[");
        var firstRound = true;
        var mergedFrames = 0;
        foreach (var pair in buckets)
        {
            var ordered = OrderedFrames(pair.Value, schema);
            mergedFrames += ordered.Count;
            if (!firstRound)
            {
                roundsJson.Append(',');
            }

            firstRound = false;
            roundsJson.Append("{\"").Append(schema.RoundField).Append("\":")
                .Append(pair.Key.ToString(CultureInfo.InvariantCulture))
                .Append(",\"").Append(schema.FramesField).Append("\":[");
            var firstFrame = true;
            foreach (var frame in ordered)
            {
                if (!firstFrame)
                {
                    roundsJson.Append(',');
                }

                firstFrame = false;
                roundsJson.Append(frame?.ToJsonString() ?? "null");
            }

            roundsJson.Append("]}");
        }

        roundsJson.Append(']');

        var merged = new JsonObject
        {
            [schema.PartitionKeyField] = incomingKey,
            [schema.RunField] = incomingRun ?? existingRun,
            [schema.StartedAtUtcField] = existing[schema.StartedAtUtcField]?.ToString()
                                          ?? incoming[schema.StartedAtUtcField]?.ToString(),
            [schema.EndedAtUtcField] = incoming[schema.EndedAtUtcField]?.ToString()
                                        ?? existing[schema.EndedAtUtcField]?.ToString(),
            [schema.RoundsField] = JsonNode.Parse(roundsJson.ToString()),
        };

        return new FramePayloadMergeResult(
            merged.ToJsonString(), existingFrames, incomingFrames, mergedFrames, replaced, unidentifiable, false);
    }

    /// <summary>轮分组桶：帧序（保序）+ 身份键 → 帧序索引（同键覆盖）。</summary>
    private sealed class FrameBucket
    {
        public List<JsonNode?> Frames { get; } = new();

        public Dictionary<string, int> Index { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>采集一载荷的帧到轮桶：身份键去重；<paramref name="replace"/> ⇒ 同键覆盖（新到胜）。</summary>
    private static void Collect(
        JsonObject root,
        FramePayloadSchema schema,
        SortedDictionary<int, FrameBucket> buckets,
        bool replace,
        ref int replaced,
        ref int unidentifiable)
    {
        if (root[schema.RoundsField] is not JsonArray rounds)
        {
            return;
        }

        var runId = root[schema.RunField]?.ToString();
        foreach (var node in rounds)
        {
            if (node is not JsonObject roundObject)
            {
                continue;
            }

            var round = int.TryParse(
                roundObject[schema.RoundField]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var number)
                ? number
                : 0;
            if (roundObject[schema.FramesField] is not JsonArray frames)
            {
                continue;
            }

            if (!buckets.TryGetValue(round, out var bucket))
            {
                bucket = new FrameBucket();
                buckets[round] = bucket;
            }

            foreach (var frame in frames)
            {
                var identityKey = frame is JsonObject frameObject
                    ? FrameIdentityReader.KeyOf(frameObject, round, runId, schema)
                    : null;
                if (identityKey is null)
                {
                    // 身份不可读（缺 seq）：回落文本键（可见化计数；不因缺身份而并帧/丢帧）。
                    unidentifiable++;
                    identityKey = "text:" + (frame?.ToJsonString() ?? string.Empty);
                }

                if (bucket.Index.TryGetValue(identityKey, out var existingIndex))
                {
                    if (replace && !SameFrame(bucket.Frames[existingIndex], frame))
                    {
                        // 仅**内容不同**才计覆盖（同身份同内容 = 幂等重提交，不产噪声账）。
                        bucket.Frames[existingIndex] = Clone(frame);
                        replaced++;
                    }

                    continue;
                }

                bucket.Index[identityKey] = bucket.Frames.Count;
                bucket.Frames.Add(Clone(frame));
            }
        }
    }

    /// <summary>同身份帧内容是否一致（幂等重提交判定；文本口径 = 载荷原文）。</summary>
    private static bool SameFrame(JsonNode? left, JsonNode? right)
        => string.Equals(
            left?.ToJsonString() ?? string.Empty,
            right?.ToJsonString() ?? string.Empty,
            StringComparison.Ordinal);

    /// <summary>按到达序数值升序稳定排序（身份不可读者保持原序靠后）。</summary>
    private static List<JsonNode?> OrderedFrames(FrameBucket bucket, FramePayloadSchema schema)
        => bucket.Frames
            .Select((frame, index) => (frame, index, seq: SeqOf(frame, schema)))
            .OrderBy(entry => entry.seq ?? long.MaxValue)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.frame)
            .ToList();

    private static long? SeqOf(JsonNode? frame, FramePayloadSchema schema)
    {
        if (frame is not JsonObject obj || obj[schema.SeqField] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
               && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int CountFrames(string payloadJson, FramePayloadSchema schema)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return 0;
        }

        try
        {
            return JsonNode.Parse(payloadJson) is JsonObject root ? CountFrames(root, schema) : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    private static int CountFrames(JsonObject root, FramePayloadSchema schema)
    {
        if (root[schema.RoundsField] is not JsonArray rounds)
        {
            return 0;
        }

        var count = 0;
        foreach (var round in rounds)
        {
            if (round is JsonObject roundObject && roundObject[schema.FramesField] is JsonArray frames)
            {
                count += frames.Count;
            }
        }

        return count;
    }

    private static JsonNode? Clone(JsonNode? node)
        => node is null ? null : JsonNode.Parse(node.ToJsonString());
}

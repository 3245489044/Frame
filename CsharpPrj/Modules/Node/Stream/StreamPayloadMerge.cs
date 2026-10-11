using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Modules.Node.Stream;

/// <summary>
/// Stream 单行载荷的**跨轮合并**（通用；域无关）：把本次 run 的聚合载荷与既有已落库单行载荷
/// **按轮合并**（本轮覆盖同轮），使「一个容器恰一行 + 载荷内 <c>rounds[]→frames[]</c>」在多次 run
/// 之间**累积**（否则每轮 upsert 会以「仅本轮帧」覆盖旧载荷 ⇒ 历史轮次丢失）。
/// <para>分区键不一致 ⇒ 不合并（返回新载荷，不把他容器数据并入）。</para>
/// </summary>
public static class StreamPayloadMerge
{
    /// <summary>合并既有载荷与本次载荷（按轮去重覆盖；返回合并后的 JSON）。</summary>
    /// <param name="existingJson">既有单行载荷 JSON（可空）。</param>
    /// <param name="incomingJson">本次 run 聚合载荷 JSON。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    public static string Merge(string? existingJson, string incomingJson, StreamPayloadSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return incomingJson;
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
            return incomingJson;
        }

        if (incoming is null)
        {
            return incomingJson;
        }

        if (existing is null)
        {
            return incomingJson;
        }

        var incomingKey = incoming[schema.PartitionKeyField]?.ToString();
        var existingKey = existing[schema.PartitionKeyField]?.ToString();
        if (!string.Equals(incomingKey, existingKey, StringComparison.Ordinal))
        {
            // 分区键不一致：不并入他容器数据（防串味）。
            return incomingJson;
        }

        var byRound = new SortedDictionary<int, JsonArray>(Comparer<int>.Default);
        Collect(existing, schema, byRound);
        Collect(incoming, schema, byRound);

        // 以**文本**重建轮数组，避免复用已挂父节点的 JsonNode（JsonNode 单父约束）。
        var roundsJson = new System.Text.StringBuilder("[");
        var first = true;
        foreach (var pair in byRound)
        {
            if (!first)
            {
                roundsJson.Append(',');
            }

            first = false;
            roundsJson.Append("{\"").Append(schema.RoundField).Append("\":").Append(pair.Key)
                .Append(",\"").Append(schema.FramesField).Append("\":")
                .Append(pair.Value.ToJsonString()).Append('}');
        }

        roundsJson.Append(']');

        var merged = new JsonObject
        {
            [schema.PartitionKeyField] = incomingKey,
            [schema.StartedAtUtcField] = existing[schema.StartedAtUtcField]?.ToString()
                                          ?? incoming[schema.StartedAtUtcField]?.ToString(),
            [schema.EndedAtUtcField] = incoming[schema.EndedAtUtcField]?.ToString()
                                        ?? existing[schema.EndedAtUtcField]?.ToString(),
            [schema.RoundsField] = JsonNode.Parse(roundsJson.ToString()),
        };
        return merged.ToJsonString();
    }

    private static void Collect(JsonObject root, StreamPayloadSchema schema, SortedDictionary<int, JsonArray> byRound)
    {
        if (root[schema.RoundsField] is not JsonArray rounds)
        {
            return;
        }

        foreach (var node in rounds)
        {
            if (node is not JsonObject roundObject)
            {
                continue;
            }

            var round = int.TryParse(roundObject[schema.RoundField]?.ToString(), out var number) ? number : 0;
            var frames = roundObject[schema.FramesField] as JsonArray ?? new JsonArray();
            if (!byRound.TryGetValue(round, out var target))
            {
                byRound[round] = CloneFrames(frames);
                continue;
            }

            // 同轮**并集去重**：以帧 JSON 为去重键（重放/重提交同帧幂等；命令面同轮多次追加不丢）。
            var seen = new HashSet<string>(
                target.Select(frame => frame?.ToJsonString() ?? string.Empty), StringComparer.Ordinal);
            foreach (var frame in frames)
            {
                var text = frame?.ToJsonString() ?? string.Empty;
                if (seen.Add(text))
                {
                    target.Add(Clone(frame));
                }
            }
        }
    }

    private static JsonArray CloneFrames(JsonArray frames)
    {
        var clone = new JsonArray();
        foreach (var frame in frames)
        {
            clone.Add(Clone(frame));
        }

        return clone;
    }

    private static JsonNode? Clone(JsonNode? node)
        => node is null ? null : JsonNode.Parse(node.ToJsonString());
}

using System.Text.Json.Nodes;

namespace Share.NodeIo;

/// <summary>帧级读记录（Stream 形态展开结果；字段袋 = 帧对象逐字段展平）。</summary>
public sealed record NodeDocumentFrame(
    int Round,
    long Seq,
    string Kind,
    IReadOnlyDictionary<string, string> Fields,
    /// <summary>帧原始 JSON（R6）：投影回写时必须保留原节点，禁由展平字段重建。</summary>
    string? RawJson = null);

/// <summary>
/// Stream 载荷 → 帧记录（读侧**唯一解析入口**，与写侧 <see cref="NodePayloadValidator"/> 同一 schema）。
/// </summary>
public static class NodePayloadReader
{
    public static bool TryReadFrames(
        NodePayloadSchema schema,
        string payloadJson,
        string partition,
        out IReadOnlyList<NodeDocumentFrame> frames,
        out string reason)
    {
        frames = Array.Empty<NodeDocumentFrame>();
        if (!NodePayloadValidator.TryParseStream(schema, payloadJson, partition, out var root, out reason))
        {
            return false;
        }

        var list = new List<NodeDocumentFrame>();
        if (schema.RoundsField is { Length: > 0 } roundsField &&
            schema.FramesField is { Length: > 0 } framesField &&
            root[roundsField] is JsonArray rounds)
        {
            foreach (var roundNode in rounds)
            {
                if (roundNode is not JsonObject round || round[framesField] is not JsonArray group)
                {
                    continue;
                }

                var roundNo = ReadInt(round[schema.RoundField ?? "round"]) ?? 0;
                foreach (var frameNode in group)
                {
                    if (frameNode is not JsonObject frame)
                    {
                        continue;
                    }

                    var fields = Flatten(frame);
                    var seq = fields.TryGetValue(schema.SeqField ?? "seq", out var seqText) &&
                              long.TryParse(seqText, out var parsedSeq)
                        ? parsedSeq
                        : 0L;
                    var kind = fields.TryGetValue(schema.KindField ?? "kind", out var kindText)
                        ? kindText
                        : string.Empty;
                    list.Add(new NodeDocumentFrame(roundNo, seq, kind, fields, frame.ToJsonString()));
                }
            }
        }
        else if (schema.FramesField is { Length: > 0 } flatField && root[flatField] is JsonArray flat)
        {
            foreach (var frameNode in flat)
            {
                if (frameNode is not JsonObject frame)
                {
                    continue;
                }

                var fields = Flatten(frame);
                var seq = fields.TryGetValue(schema.SeqField ?? "seq", out var seqText) &&
                          long.TryParse(seqText, out var parsedSeq)
                    ? parsedSeq
                    : 0L;
                var kind = fields.TryGetValue(schema.KindField ?? "kind", out var kindText)
                    ? kindText
                    : string.Empty;
                list.Add(new NodeDocumentFrame(0, seq, kind, fields, frame.ToJsonString()));
            }
        }

        frames = list;
        return true;
    }

    private static int? ReadInt(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

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
}

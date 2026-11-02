using System.Globalization;
using System.Text.Json.Nodes;
using Modules.Node.Projection;

namespace Share.NodeIo;

/// <summary>
/// Stream 帧行投影：单行聚合载荷 → 帧级行（与 <see cref="NodePayloadReader"/> 同源解析）。
/// 解析失败 ⇒ 原样保留文档行（原因由消费方按 <c>payload-rejected</c> 上送，不静默丢）。
/// </summary>
public sealed class StreamFramesProjection : INodeDocumentProjection
{
    public string Id => NodeDocumentProjectionRegistry.StreamFramesId;

    public IReadOnlyList<NodeProjectionItem> Project(
        NodeDocumentScope scope,
        ResolvedPlacement placement,
        IReadOnlyList<NodeProjectionItem> items,
        NodePayloadSchema schema)
    {
        var projected = new List<NodeProjectionItem>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.PayloadJson) ||
                !NodePayloadReader.TryReadFrames(schema, item.PayloadJson, scope.KeyContext, out var frames, out _))
            {
                projected.Add(item);
                continue;
            }

            if (frames.Count == 0)
            {
                projected.Add(item);
                continue;
            }

            foreach (var frame in frames)
            {
                var fields = new Dictionary<string, string>(frame.Fields, StringComparer.Ordinal)
                {
                    ["round"] = frame.Round.ToString(CultureInfo.InvariantCulture),
                    ["seq"] = frame.Seq.ToString(CultureInfo.InvariantCulture),
                };
                var frameJson = new JsonObject();
                foreach (var pair in fields)
                {
                    frameJson[pair.Key] = pair.Value;
                }

                projected.Add(new NodeProjectionItem(
                    $"{item.Key}#r{frame.Round}:{frame.Seq}",
                    placement.PlacementId,
                    fields,
                    item.Origin,
                    frameJson.ToJsonString(),
                    item.DomainKey,
                    item.SourceKey ?? item.Key,
                    item.WriteDomain,
                    item.Address,
                    item.Tags,
                    item.Signal,
                    item.RecordMeta));
            }
        }

        return projected;
    }
}

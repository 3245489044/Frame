using System.Globalization;
using System.Text.Json.Nodes;
using Modules.Node.Projection;

namespace Share.NodeIo;

/// <summary>
/// Stream 帧行投影：单行聚合载荷 → 帧级行（与 <see cref="NodePayloadReader"/> 同源解析）。
/// <para>
/// 解析失败 ⇒ 原样保留文档行，但**显式标记** <see cref="NodeProjectionItem.ProjectionStatus"/>
/// （<c>payload-rejected:&lt;reason&gt;</c>），结果层并入 Reasons；禁把失败与「原样文档」混为一谈（E9）。
/// 帧行载荷 = 帧原始 JSON（<see cref="NodeDocumentFrame.RawJson"/>），禁由展平字段重建（R6）。
/// </para>
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
            var reason = NodeDocumentReasons.PayloadMissing;
            IReadOnlyList<NodeDocumentFrame> frames = Array.Empty<NodeDocumentFrame>();
            if (string.IsNullOrWhiteSpace(item.PayloadJson) ||
                !NodePayloadReader.TryReadFrames(schema, item.PayloadJson, scope.KeyContext, out frames, out reason))
            {
                projected.Add(MarkFailed(item, reason));
                continue;
            }

            if (frames.Count == 0)
            {
                // 合法空（无轮/无帧）：不标记失败，由读面按 OkButEmpty 归因。
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

                projected.Add(new NodeProjectionItem(
                    $"{item.Key}#r{frame.Round}:{frame.Seq}",
                    placement.PlacementId,
                    fields,
                    item.Origin,
                    frame.RawJson ?? FrameJson(fields),
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

    private static string FrameJson(IReadOnlyDictionary<string, string> fields)
    {
        var json = new JsonObject();
        foreach (var pair in fields)
        {
            json[pair.Key] = pair.Value;
        }

        return json.ToJsonString();
    }

    private static NodeProjectionItem MarkFailed(NodeProjectionItem item, string reason)
        => item with
        {
            ProjectionStatus = NodeDocumentReasons.ProjectionPayloadRejected + ":" +
                               (string.IsNullOrWhiteSpace(reason) ? NodeDocumentReasons.PayloadMissing : reason),
        };
}

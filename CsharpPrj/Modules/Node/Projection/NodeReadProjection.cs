using System.Text.Json.Nodes;
using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
using Modules.Node.Placement;
using Modules.Node.Spec;

namespace Modules.Node.Projection;

/// <summary>运行态来源（装配优先级 Live &gt; Snapshot &gt; Derived，按来源优先级做<b>字段级</b>叠加）。</summary>
public enum ProjectionOrigin
{
    /// <summary>内存装配（如 LoopFoldMethod 的节点装配结果），不落库。</summary>
    Live,

    /// <summary>已物化（载体可读）。</summary>
    Snapshot,

    /// <summary>投影/聚合派生。</summary>
    Derived,
}

public sealed record NodeProjectionItem(
    string Key,
    string PlacementId,
    IReadOnlyDictionary<string, string> Fields,
    ProjectionOrigin Origin,
    string? PayloadJson = null);

/// <summary>读投影范围（裁剪只在读侧：N7）。</summary>
public sealed record ReadScope(
    string? Domain = null,
    IReadOnlyList<string>? PlacementIds = null,
    string? SelectionId = null,
    string? KeyPrefix = null,
    int? Limit = null);

/// <summary>
/// 读投影：<c>Scope → placements → 解码 → selection → 排序 → Origin 标注</c>。
/// 顺序由键内序数决定（N6：删除全部边后顺序不变，枚举序 = 键序数序）；无来源命中 ⇒ ∅（不伪造空闲态）。
/// 不写库。
/// </summary>
public sealed class NodeReadProjection
{
    private readonly PlacementRegistry _placements;
    private readonly ICarrierRegistry _carriers;
    private readonly SelectionSet _selections;
    private readonly NodeFormCodec _codec;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeReadProjection(
        PlacementRegistry placements,
        ICarrierRegistry carriers,
        SelectionSet selections,
        NodeFormCodec? codec = null,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _placements = placements;
        _carriers = carriers;
        _selections = selections;
        _codec = codec ?? new NodeFormCodec();
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public async Task<IReadOnlyList<NodeProjectionItem>> ProjectAsync(
        ReadScope scope,
        IReadOnlyList<NodeProjectionItem>? overlays = null,
        CancellationToken cancellationToken = default)
    {
        var targets = ResolvePlacements(scope);
        var items = new List<NodeProjectionItem>();

        foreach (var placement in targets)
        {
            if (!_carriers.TryGet(placement.CarrierId, out var carrier))
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "carrier.unbound",
                    NodeDiagnosticSeverity.Warning,
                    $"载体 '{placement.CarrierId}' 未注册：该位置读投影为空（不阻断其它位置）",
                    placement.PlacementId));
                continue;
            }

            var keys = await carrier.EnumerateKeysAsync(scope.KeyPrefix, cancellationToken).ConfigureAwait(false);

            foreach (var key in keys)
            {
                if (scope.KeyPrefix is { Length: > 0 } prefix && !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var record = await carrier.ReadAsync(key, cancellationToken).ConfigureAwait(false);
                if (record is null)
                {
                    continue;
                }

                var payload = _codec.Decode(placement.Form, record.Json);
                items.Add(new NodeProjectionItem(key, placement.PlacementId, Flatten(payload), ProjectionOrigin.Snapshot, payload));
            }
        }

        var merged = Overlay(items, overlays ?? Array.Empty<NodeProjectionItem>());
        var filtered = ApplySelection(merged, targets, scope);

        var ordered = filtered
            .OrderBy(item => item.Key, NodeKeyOrdinalComparer.Instance)
            .ThenBy(item => item.PlacementId, StringComparer.Ordinal)
            .ToArray();

        var limited = scope.Limit is { } limit && limit > 0 && ordered.Length > limit
            ? ordered.Take(limit).ToArray()
            : ordered;

        if (limited.Length == 0)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "projection.empty",
                NodeDiagnosticSeverity.Info,
                "读投影无来源命中：返回 ∅（不伪造状态）",
                scope.Domain ?? scope.PlacementIds?.FirstOrDefault() ?? scope.KeyPrefix ?? string.Empty));
        }

        return limited;
    }

    private IReadOnlyList<PlacementSpec> ResolvePlacements(ReadScope scope)
    {
        if (scope.PlacementIds is { Count: > 0 })
        {
            var selected = new List<PlacementSpec>();
            foreach (var id in scope.PlacementIds)
            {
                if (_placements.TryGet(id, out var spec) && spec.IsReadable)
                {
                    selected.Add(spec);
                }
            }

            return selected;
        }

        return scope.Domain is { Length: > 0 } domain
            ? _placements.ReadableByDomain(domain)
            : _placements.All.Where(p => p.IsReadable).ToArray();
    }

    private IReadOnlyList<NodeProjectionItem> ApplySelection(
        IReadOnlyList<NodeProjectionItem> items,
        IReadOnlyList<PlacementSpec> targets,
        ReadScope scope)
    {
        var result = new List<NodeProjectionItem>();

        foreach (var item in items)
        {
            var selectionId = scope.SelectionId
                              ?? targets.FirstOrDefault(p => string.Equals(p.PlacementId, item.PlacementId, StringComparison.Ordinal))?.SelectionId
                              ?? ExpressionSelectionPredicate.AllSelectionId;

            if (_selections.Accept(selectionId, item.Fields))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>按来源优先级做字段级叠加（低优先级填空，高优先级非空覆盖）。</summary>
    private static IReadOnlyList<NodeProjectionItem> Overlay(
        IReadOnlyList<NodeProjectionItem> snapshotItems,
        IReadOnlyList<NodeProjectionItem> overlays)
    {
        var order = new[] { ProjectionOrigin.Derived, ProjectionOrigin.Snapshot, ProjectionOrigin.Live };
        var buckets = new Dictionary<string, List<NodeProjectionItem>>(StringComparer.Ordinal);

        foreach (var item in snapshotItems.Concat(overlays))
        {
            var identity = item.Key + "|" + item.PlacementId;
            if (!buckets.TryGetValue(identity, out var list))
            {
                list = new List<NodeProjectionItem>();
                buckets[identity] = list;
            }

            list.Add(item);
        }

        var merged = new List<NodeProjectionItem>();

        foreach (var bucket in buckets.Values)
        {
            var ordered = bucket.OrderBy(item => Array.IndexOf(order, item.Origin)).ToArray();
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string? payload = null;
            var origin = ordered[0].Origin; // 初值仅为确定赋值；循环内必覆盖为最高优先级来源

            foreach (var item in ordered)
            {
                foreach (var pair in item.Fields)
                {
                    // 非空覆盖（字段级叠加语义）：高优先级来源的空值不抹掉低优先级非空值
                    if (string.IsNullOrEmpty(pair.Value) && fields.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    fields[pair.Key] = pair.Value;
                }

                if (!string.IsNullOrEmpty(item.PayloadJson))
                {
                    payload = item.PayloadJson;
                }

                origin = item.Origin;
            }

            merged.Add(new NodeProjectionItem(ordered[0].Key, ordered[0].PlacementId, fields, origin, payload));
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> Flatten(string payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // 解析宽容度单源 = NodeJson（与写侧一致：尾逗号/注释跳过）
        if (!NodeJson.TryParse(payload, out var node, out _) || node is not JsonObject obj)
        {
            // 非 JSON 载荷：字段面为空（读侧不伪造结构）
            return fields;
        }

        foreach (var pair in obj)
        {
            if (pair.Value is null)
            {
                continue;
            }

            fields[pair.Key] = NodeJson.ValueToText(pair.Value);
        }

        return fields;
    }
}

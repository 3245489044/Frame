using Modules.Node.Gather;
using Modules.Node.Projection;

namespace Modules.Node.Stream;

/// <summary>帧级塑形单元（中性；域无关）：<see cref="Seq"/> = 容器级到达序，塑形字段由声明面驱动。</summary>
public sealed record StreamShapedFrame(
    string Kind,
    string? Badge,
    string? Title,
    string? Summary,
    long Seq,
    string? AtUtc);

/// <summary>一轮内的帧族分组（中性）：族键 + 表头 + 帧序列（按 <c>seq</c> 升序）。</summary>
public sealed record StreamFamilyGroup(
    string Family,
    string Label,
    int Count,
    IReadOnlyList<StreamShapedFrame> Frames);

/// <summary>帧级轮分组（中性）：轮升序；组内族按声明序。</summary>
public sealed record StreamRoundGroup(int Round, IReadOnlyList<StreamFamilyGroup> Groups);

/// <summary>
/// 帧级投影结果（中性；域无关）：<see cref="Reasons"/> 非空 ⇒ 不产出（不采信/⊥），调用方按空/⊥处理。
/// </summary>
/// <param name="Rounds">轮分组序列（按轮升序）。</param>
/// <param name="Total">选中帧数之和。</param>
/// <param name="StartedAtUtc">首次成轮时刻（可空）。</param>
/// <param name="EndedAtUtc">最后一帧到达时刻（可空）。</param>
/// <param name="Reasons">可见化原因（空 = 产出）。</param>
public sealed record StreamProjectionResult(
    IReadOnlyList<StreamRoundGroup> Rounds,
    int Total,
    string? StartedAtUtc,
    string? EndedAtUtc,
    IReadOnlyList<string> Reasons)
{
    /// <summary>是否产出（无原因）。</summary>
    public bool HasModel => Reasons.Count == 0;
}

/// <summary>
/// Stream **帧级投影**（通用；域无关）：单行载荷 → 帧级选择 → 归族/塑形 → 中性轮/族/帧模型。
/// <para>
/// 归族/塑形**复用**声明面（<see cref="NodeGatherSpec"/> + <see cref="DeclaredShaper"/>）：与面板路径同源，避免两套规则漂移。
/// 差异参数 = 显示键前缀 + 位置 id + 载荷字段面。<b>零领域 DTO</b>：输出为中性记录，域 DTO 由上层映射。
/// </para>
/// </summary>
public sealed class StreamFrameProjection
{
    private readonly NodeGatherSpec _spec;
    private readonly StreamFrameSelector _selector;
    private readonly DeclaredShaper _shaper;
    private readonly string _displayKeyPrefix;
    private readonly string _placementId;
    private readonly StreamPayloadSchema _schema;

    /// <param name="spec">帧级聚拢声明（族/塑形轴）。</param>
    /// <param name="selector">帧级选择应用器（声明面单源）。</param>
    /// <param name="displayKeyPrefix">合成显示键族前缀（如 <c>obs.</c>）。</param>
    /// <param name="placementId">落库位置 id（帧项携带）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    public StreamFrameProjection(
        NodeGatherSpec spec,
        StreamFrameSelector selector,
        string displayKeyPrefix,
        string placementId,
        StreamPayloadSchema schema)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _shaper = new DeclaredShaper(_spec);
        _displayKeyPrefix = displayKeyPrefix ?? throw new ArgumentNullException(nameof(displayKeyPrefix));
        _placementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    /// <summary>
    /// 投影单行载荷。不采信（载荷非法/分区键不一致）或选择不可用（⊥）⇒ <c>Reasons</c> 非空 + 空轮（不伪造）。
    /// </summary>
    /// <param name="payloadJson">单行载荷 JSON（可空）。</param>
    /// <param name="container">容器（分区键）。</param>
    /// <param name="selectionId">帧级选择名（缺省 = 全量）。</param>
    public StreamProjectionResult Project(
        string? payloadJson, string container, string? selectionId = null)
    {
        if (!StreamPayloadReader.TryRead(payloadJson, container, _schema, out var payload, out var readReason))
        {
            return Empty(new[] { readReason! });
        }

        if (!_selector.TryResolve(selectionId, out var predicate, out var selectionReason))
        {
            return Empty(new[] { selectionReason! });
        }

        var selected = payload!.Frames
            .Where(frame => predicate!.Accept(frame.Fields))
            .ToArray();

        var rounds = selected
            .GroupBy(frame => frame.Round)
            .OrderBy(group => group.Key)
            .Select(group => new StreamRoundGroup(group.Key, BuildGroups(group.ToArray(), container)))
            .ToArray();

        return new StreamProjectionResult(
            rounds,
            selected.Length,
            payload.StartedAtUtc,
            payload.EndedAtUtc,
            Array.Empty<string>());
    }

    private static StreamProjectionResult Empty(IReadOnlyList<string> reasons)
        => new(Array.Empty<StreamRoundGroup>(), 0, null, null, reasons);

    private StreamFamilyGroup[] BuildGroups(IReadOnlyList<StreamFrameRecord> frames, string taskKey)
        => frames
            .GroupBy(frame => _spec.FamilyOf(frame.Kind))
            .OrderBy(group => FamilyOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(frame => frame.Seq).Select(frame => Shape(frame, taskKey)).ToArray();
                return new StreamFamilyGroup(group.Key, group.Key, ordered.Length, ordered);
            })
            .ToArray();

    private int FamilyOrder(string family)
    {
        var index = 0;
        foreach (var name in _spec.FamilyOrder)
        {
            if (string.Equals(name, family, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return _spec.FamilyOrder.Count;
    }

    private StreamShapedFrame Shape(StreamFrameRecord frame, string taskKey)
    {
        var item = new NodeProjectionItem(
            StreamFrameKeys.DisplayKey(_displayKeyPrefix, taskKey, frame.Seq),
            _placementId,
            frame.Fields,
            ProjectionOrigin.Derived);
        var shape = _shaper.Shape(item);
        var atUtc = shape?.Updated ??
                    (frame.Fields.TryGetValue(_schema.AtUtcField, out var value) ? value : null);
        return new StreamShapedFrame(
            frame.Kind, shape?.Badge, shape?.Title, shape?.Summary, frame.Seq, atUtc);
    }
}

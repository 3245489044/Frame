using Modules.Node.Gather;
using Modules.Node.Projection;

namespace Modules.Node.Stream;

/// <summary>
/// Stream 域**帧级读适配器**（通用；域无关）：内包 <see cref="INodeReadAdapter"/>（读单行聚合节点），
/// 把单行载荷经 <see cref="StreamPayloadReader"/> 解析、<see cref="StreamFrameSelector"/> 帧级选择后，
/// **展平为帧级 <see cref="NodeProjectionItem"/> 序列**（合成显示键仅读侧内存合成）。
/// <para>
/// 存在理由：聚拢引擎按「一节点一行」分组，而 Stream 域是「一任务一行 + 载荷内 N 帧」⇒
/// 展开在**适配段**完成，引擎与声明面零改。
/// </para>
/// <para>
/// 三态（F-4）：内包 <c>Unavailable</c> ⇒ 原样透传（唯一允许回退态）；域已装配但无行/无帧/载荷不采信 ⇒
/// <c>OkButEmpty</c>（**不清面**）；选择不可用（⊥）⇒ 空 + 原因。
/// </para>
/// </summary>
public sealed class StreamFrameReadAdapter : INodeReadAdapter
{
    private readonly INodeReadAdapter _inner;
    private readonly StreamFrameSelector _selector;
    private readonly IFrameKeyRenderer _renderer;
    private readonly string _familyId;
    private readonly string _placementId;
    private readonly StreamPayloadSchema _schema;
    private readonly string? _frameSelectionId;
    private readonly Func<string, StreamFrameRecord, string> _displayKeyOf;
    private readonly string? _domainKey;

    /// <param name="inner">单行读适配器（该 Stream 域，节点级 selection 固定 <c>all</c>）。</param>
    /// <param name="selector">帧级选择应用器（声明面单源）。</param>
    /// <param name="renderer">帧级键渲染端口（实现 = 声明驱动；框架不内置键形）。</param>
    /// <param name="familyId">键族 id（声明面单源）。</param>
    /// <param name="placementId">落库位置 id（帧项携带）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    /// <param name="frameSelectionId">帧级选择名（缺省 = 全量）。</param>
    /// <param name="displayKeyOf">帧级显示键工厂（缺省 = 声明面渲染；域仅在**显式迁移期**注入）。</param>
    /// <param name="domainKey">所属域键（用于回写行身份：帧项 <c>DomainKey</c>）；缺省 null（由消费方回退域键）。</param>
    public StreamFrameReadAdapter(
        INodeReadAdapter inner,
        StreamFrameSelector selector,
        IFrameKeyRenderer renderer,
        string familyId,
        string placementId,
        StreamPayloadSchema schema,
        string? frameSelectionId = null,
        Func<string, StreamFrameRecord, string>? displayKeyOf = null,
        string? domainKey = null)
    {
        _domainKey = string.IsNullOrWhiteSpace(domainKey) ? null : domainKey;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        _familyId = familyId;
        _placementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _frameSelectionId = frameSelectionId;
        _displayKeyOf = displayKeyOf
            ?? ((task, frame) => _renderer.Render(_familyId, task, frame.Identity));
    }

    /// <inheritdoc />
    public async Task<NodeGatherRead> ReadAsync(string container, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(container, cancellationToken).ConfigureAwait(false);
        if (read.Outcome.IsUnavailable)
        {
            return new NodeGatherRead(
                NodeGatherOutcome.Unavailable(read.Outcome.Reasons), Array.Empty<NodeProjectionItem>());
        }

        var reasons = new List<string>(read.Outcome.Reasons);
        if (!_selector.TryResolve(_frameSelectionId, out var predicate, out var selectionReason))
        {
            reasons.Add(selectionReason!);
            return new NodeGatherRead(
                NodeGatherOutcome.Available(0, reasons.ToArray()), Array.Empty<NodeProjectionItem>());
        }

        var task = container ?? string.Empty;
        var items = new List<NodeProjectionItem>();
        foreach (var row in read.Items)
        {
            if (!StreamPayloadReader.TryRead(row.PayloadJson, task, _schema, out var payload, out var readReason))
            {
                reasons.Add(readReason!);
                continue;
            }

            foreach (var frame in payload!.Frames)
            {
                if (!predicate!.Accept(frame.Fields))
                {
                    continue;
                }

                items.Add(new NodeProjectionItem(
                    _displayKeyOf(task, frame),
                    _placementId,
                    frame.Fields,
                    row.Origin,
                    // 真键 = 被展开聚合行的 Key（删除/写回按聚合真行，非合成显示键）
                    DomainKey: _domainKey,
                    SourceKey: row.Key));
            }
        }

        var normalized = NodeReadNormalizer.Normalize(items);
        return new NodeGatherRead(
            NodeGatherOutcome.Available(normalized.Count, reasons.ToArray()), normalized);
    }
}

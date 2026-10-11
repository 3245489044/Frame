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
    private readonly string _displayKeyPrefix;
    private readonly string _placementId;
    private readonly StreamPayloadSchema _schema;
    private readonly string? _frameSelectionId;
    private readonly Func<string, StreamFrameRecord, string> _displayKeyOf;

    /// <param name="inner">单行读适配器（该 Stream 域，节点级 selection 固定 <c>all</c>）。</param>
    /// <param name="selector">帧级选择应用器（声明面单源）。</param>
    /// <param name="displayKeyPrefix">合成显示键族前缀（如 <c>obs.</c>）。</param>
    /// <param name="placementId">落库位置 id（帧项携带）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    /// <param name="frameSelectionId">帧级选择名（缺省 = 全量）。</param>
    /// <param name="displayKeyOf">帧级显示键工厂（缺省 = <c>{prefix}{task}.f{seq}</c>）；域可注入既有形状以保读侧消费方/锚点幂等。</param>
    public StreamFrameReadAdapter(
        INodeReadAdapter inner,
        StreamFrameSelector selector,
        string displayKeyPrefix,
        string placementId,
        StreamPayloadSchema schema,
        string? frameSelectionId = null,
        Func<string, StreamFrameRecord, string>? displayKeyOf = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _displayKeyPrefix = displayKeyPrefix ?? throw new ArgumentNullException(nameof(displayKeyPrefix));
        _placementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _frameSelectionId = frameSelectionId;
        _displayKeyOf = displayKeyOf
            ?? ((task, frame) => StreamFrameKeys.DisplayKey(_displayKeyPrefix, task, frame.Seq));
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
                    row.Origin));
            }
        }

        var normalized = NodeReadNormalizer.Normalize(items);
        return new NodeGatherRead(
            NodeGatherOutcome.Available(normalized.Count, reasons.ToArray()), normalized);
    }
}

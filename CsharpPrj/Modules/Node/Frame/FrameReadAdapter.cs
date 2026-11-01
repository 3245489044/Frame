using System;
using System.Collections.Generic;
using Modules.Node.Gather;
using Modules.Node.Projection;

namespace Modules.Node.Frame;

/// <summary>
/// Stream 域**帧级读适配器**（通用；域无关）：内包 <see cref="INodeReadAdapter"/>（读单行聚合节点），
/// 把单行载荷经 <see cref="FramePayloadReader"/> 解析、<see cref="FrameSelector"/> 帧级选择后，
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
public sealed class FrameReadAdapter : INodeReadAdapter
{
    private readonly INodeReadAdapter _inner;
    private readonly FrameSelector _selector;
    private readonly IFrameKeyRenderer _renderer;
    private readonly string _familyId;
    private readonly string _placementId;
    private readonly FramePayloadSchema _schema;
    private readonly string? _frameSelectionId;
    private readonly Func<string, FrameRecord, string> _displayKeyOf;
    private readonly string? _domainKey;
    private readonly Func<string, string?>? _containerOf;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? _classify;

    /// <param name="inner">单行读适配器（该 Stream 域，节点级 selection 固定 <c>all</c>）。</param>
    /// <param name="selector">帧级选择应用器（声明面单源）。</param>
    /// <param name="renderer">帧级键渲染端口（实现 = 声明驱动；框架不内置键形）。</param>
    /// <param name="familyId">键族 id（声明面单源）。</param>
    /// <param name="placementId">落库位置 id（帧项携带）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    /// <param name="frameSelectionId">帧级选择名（缺省 = 全量）。</param>
    /// <param name="displayKeyOf">帧级显示键工厂（缺省 = 声明面渲染；域仅在**显式迁移期**注入）。</param>
    /// <param name="domainKey">所属域键（用于回写行身份：帧项 <c>DomainKey</c>）；缺省 null（由消费方回退域键）。</param>
    /// <param name="containerOf">跨容器读的**行容器解析器**（键 → 容器；单源由调用方注入）。
    /// 给出且请求容器为空时（<c>read-source=full</c> 全量读），逐行解析自身容器；解析失败 ⇒ 该行跳过并可见化（不猜）。</param>
    /// <param name="frameClassify">帧字段袋**分类器**（声明驱动的呈现类注入；缺省 null = 不改字段）。
    /// 分类先于选择求值 ⇒ 选择表达式可直接用 <c>presentation hasany (…)</c>，无需按 kind 集合写第二张表。</param>
    public FrameReadAdapter(
        INodeReadAdapter inner,
        FrameSelector selector,
        IFrameKeyRenderer renderer,
        string familyId,
        string placementId,
        FramePayloadSchema schema,
        string? frameSelectionId = null,
        Func<string, FrameRecord, string>? displayKeyOf = null,
        string? domainKey = null,
        Func<string, string?>? containerOf = null,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? frameClassify = null)
    {
        _domainKey = string.IsNullOrWhiteSpace(domainKey) ? null : domainKey;
        _containerOf = containerOf;
        _classify = frameClassify;
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

        var requestedContainer = container ?? string.Empty;
        var items = new List<NodeProjectionItem>();
        foreach (var row in read.Items)
        {
            // 容器来源（单源）：显式请求优先；全量读（请求容器为空）⇒ 逐行解析自身容器（键族解析由调用方注入）。
            var rowContainer = requestedContainer;
            if (string.IsNullOrWhiteSpace(rowContainer))
            {
                rowContainer = _containerOf?.Invoke(row.Key) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rowContainer))
                {
                    reasons.Add(
                        $"{_schema.Label}.container-unresolved：行 '{row.Key}' 无法解析容器（跨容器读跳过该行）");
                    continue;
                }
            }

            if (!FramePayloadReader.TryRead(row.PayloadJson, rowContainer, _schema, out var payload, out var readReason))
            {
                reasons.Add(readReason!);
                continue;
            }

            foreach (var frame in payload!.Frames)
            {
                var fields = _classify is null ? frame.Fields : _classify(frame.Fields);
                if (!predicate!.Accept(fields))
                {
                    continue;
                }

                items.Add(new NodeProjectionItem(
                    _displayKeyOf(rowContainer, frame),
                    _placementId,
                    WithPartitionKey(fields, rowContainer),
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

    /// <summary>
    /// 帧字段袋补**分区键**（容器 = 聚合行归属）：结构事实随行注入，供声明面 <c>group-by</c> / 呈现使用。
    /// 已同值 ⇒ 原样返回（零分配）；字段名由载荷 schema 单源给出（禁硬编码领域字段名）。
    /// </summary>
    private IReadOnlyDictionary<string, string> WithPartitionKey(
        IReadOnlyDictionary<string, string> fields, string container)
    {
        var key = _schema.PartitionKeyField;
        if (string.IsNullOrWhiteSpace(key)
            || (fields.TryGetValue(key, out var existing) && string.Equals(existing, container, StringComparison.Ordinal)))
        {
            return fields;
        }

        var merged = new Dictionary<string, string>(fields, StringComparer.Ordinal) { [key] = container };
        return merged;
    }
}

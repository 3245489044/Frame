using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **声明投影**读适配器（框架级、零领域）：读项序列 →（声明）折叠 →（声明）窗口 → 读项序列。
/// <para>
/// 折叠：按 <see cref="NodeGatherProjection.Identity"/> 字段值组键；<c>count</c> 保留重复计数
/// （写 <see cref="NodeGatherProjection.RepeatCountField"/>，呈现层渲染 ×N），<c>first</c>/<c>last</c> 保留首/末条。
/// 身份字段缺失 ⇒ **不折叠**且可见化（不静默并行）。
/// </para>
/// <para>
/// 窗口：按分区字段（缺省 <c>taskKey</c>）限每组行数；超限截断并可见化（分页/上限是声明事实，不由视图各写）。
/// 投影未声明 ⇒ 原样透传（零回归）。
/// </para>
/// </summary>
public sealed class DeclaredProjectionReadAdapter : INodeReadAdapter
{
    private readonly INodeReadAdapter _inner;
    private readonly NodeGatherProjection? _projection;
    private readonly string _partitionField;
    private readonly string _label;

    /// <param name="inner">内层适配器（帧级展开/域读等）。</param>
    /// <param name="projection">投影声明（null / 非生效 ⇒ 原样透传）。</param>
    /// <param name="partitionField">分区字段名（窗口按它分组；缺省 <c>taskKey</c>）。</param>
    /// <param name="label">可见化原因前缀（域可辨）。</param>
    public DeclaredProjectionReadAdapter(
        INodeReadAdapter inner,
        NodeGatherProjection? projection,
        string partitionField = "taskKey",
        string label = "projection")
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _projection = projection;
        _partitionField = string.IsNullOrWhiteSpace(partitionField) ? "taskKey" : partitionField;
        _label = string.IsNullOrWhiteSpace(label) ? "projection" : label;
    }

    /// <inheritdoc />
    public async Task<NodeGatherRead> ReadAsync(string container, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(container, cancellationToken).ConfigureAwait(false);
        if (_projection is null || read.Outcome.IsUnavailable)
        {
            return read;
        }

        var reasons = new List<string>(read.Outcome.Reasons);
        var projected = _projection.IsActive ? Collapse(read.Items, reasons) : read.Items;
        projected = ApplyWindow(projected, reasons);
        return new NodeGatherRead(
            NodeGatherOutcome.Available(projected.Count, reasons.ToArray()), projected);
    }

    /// <summary>按声明身份键折叠（保首次出现序；缺失身份 ⇒ 不折叠 + 可见化一次）。</summary>
    private IReadOnlyList<NodeProjectionItem> Collapse(
        IReadOnlyList<NodeProjectionItem> items, List<string> reasons)
    {
        var output = new List<NodeProjectionItem>(items.Count);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnedMissing = false;

        foreach (var item in items)
        {
            var identity = IdentityOf(item, out var missingField);
            if (identity is null)
            {
                if (!warnedMissing)
                {
                    reasons.Add(
                        $"{_label}.identity-missing：字段 '{missingField}' 缺失/空白，该行不参与折叠（不猜身份）");
                    warnedMissing = true;
                }

                output.Add(item);
                continue;
            }

            if (positions.TryGetValue(identity, out var position))
            {
                var existing = output[position];
                var count = CountOf(existing) + 1;
                var kept = string.Equals(_projection!.Collapse, "last", StringComparison.Ordinal) ? item : existing;
                output[position] = WithCount(kept, count);
                continue;
            }

            positions[identity] = output.Count;
            output.Add(_projection!.KeepsCount ? WithCount(item, 1) : item);
        }

        return output;
    }

    /// <summary>按分区字段限每组行数（保序；截断可见化）。未声明窗口 ⇒ 原样。</summary>
    private IReadOnlyList<NodeProjectionItem> ApplyWindow(
        IReadOnlyList<NodeProjectionItem> items, List<string> reasons)
    {
        var window = _projection?.Window;
        if (window is not > 0)
        {
            return items;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var truncated = new Dictionary<string, int>(StringComparer.Ordinal);
        var output = new List<NodeProjectionItem>(items.Count);
        foreach (var item in items)
        {
            var partition = item.Fields.TryGetValue(_partitionField, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : string.Empty;
            counts.TryGetValue(partition, out var used);
            if (used >= window.Value)
            {
                truncated[partition] = truncated.TryGetValue(partition, out var dropped) ? dropped + 1 : 1;
                continue;
            }

            counts[partition] = used + 1;
            output.Add(item);
        }

        foreach (var pair in truncated)
        {
            reasons.Add(
                $"{_label}.window-truncated：组 '{pair.Key}' 超上限 {window.Value}（已截断 {pair.Value} 行，可见化）");
        }

        return output;
    }

    /// <summary>身份键 = 声明字段值用 <c>|</c> 连接；任一字段缺失/空白 ⇒ null + 缺失字段名。</summary>
    private string? IdentityOf(NodeProjectionItem item, out string? missingField)
    {
        missingField = null;
        var parts = new List<string>(_projection!.Identity.Count);
        foreach (var field in _projection.Identity)
        {
            if (!item.Fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                missingField = field;
                return null;
            }

            parts.Add(value);
        }

        return string.Join('|', parts);
    }

    /// <summary>已折叠行的重复计数（缺省 1）。</summary>
    private static int CountOf(NodeProjectionItem item)
        => item.Fields.TryGetValue(NodeGatherProjection.RepeatCountField, out var raw) &&
           int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : 1;

    /// <summary>写回重复计数（字段袋合并；不改键/来源身份）。</summary>
    private static NodeProjectionItem WithCount(NodeProjectionItem item, int count)
    {
        var merged = new Dictionary<string, string>(item.Fields, StringComparer.Ordinal)
        {
            [NodeGatherProjection.RepeatCountField] =
                count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return item with { Fields = merged };
    }
}

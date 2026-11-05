using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **组合读适配器**（通用）：把**多个读来源**收敛为**单一读事实面** —— 各源读项经
/// <see cref="NodeReadNormalizer"/> 并集去重保序，三态按「**全不可用才不可用**」合并。
/// <para>
/// 域无关：来源由业务侧注入（每源一个 <see cref="INodeReadAdapter"/>，如「按域读端口」多个）。
/// 治「多域/多来源拼装各写一套去重」：去重 / 去空 / 保序唯一入口 = <see cref="NodeReadNormalizer"/>。
/// </para>
/// </summary>
public sealed class CompositeNodeReadAdapter : INodeReadAdapter
{
    private readonly IReadOnlyList<INodeReadAdapter> _sources;

    /// <param name="sources">来源适配器（≥1；各源独立读，结果并集）。</param>
    public CompositeNodeReadAdapter(params INodeReadAdapter[] sources)
        : this((IReadOnlyList<INodeReadAdapter>)(sources
            ?? throw new ArgumentNullException(nameof(sources))))
    {
    }

    /// <param name="sources">来源适配器（≥1；各源独立读，结果并集）。</param>
    public CompositeNodeReadAdapter(IReadOnlyList<INodeReadAdapter> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("组合读适配器至少需要一个来源", nameof(sources));
        }

        _sources = sources;
    }

    /// <inheritdoc />
    public async Task<NodeGatherRead> ReadAsync(string container, CancellationToken cancellationToken = default)
    {
        var items = new List<NodeProjectionItem>();
        var reasons = new List<string>();
        var anyAvailable = false;

        foreach (var source in _sources)
        {
            var read = await source.ReadAsync(container, cancellationToken).ConfigureAwait(false);
            if (read is null)
            {
                continue;
            }

            if (read.Outcome.IsOk)
            {
                anyAvailable = true;
            }

            items.AddRange(read.Items);
            if (read.Outcome.Reasons.Count > 0)
            {
                reasons.AddRange(read.Outcome.Reasons);
            }
        }

        var normalized = NodeReadNormalizer.Normalize(items);

        // 全不可用 ⇒ 不可用（唯一允许回退的态）；任一可用 ⇒ 按并集行数归 Ok / OkButEmpty（空集不清面）。
        return anyAvailable
            ? new NodeGatherRead(NodeGatherOutcome.Available(normalized.Count, reasons), normalized)
            : new NodeGatherRead(NodeGatherOutcome.Unavailable(reasons), Array.Empty<NodeProjectionItem>());
    }
}

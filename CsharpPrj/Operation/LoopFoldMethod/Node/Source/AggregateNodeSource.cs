using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Node.Model;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Node.Source;

/// <summary>
/// 聚合节点源 —— 把多个 <see cref="INodeSource{TState,TVertex}"/> 收集结果合并为一个有序节点集。
/// 保持各源相对顺序（先来先序）；单源 null/空忽略。供 <see cref="NodeBuilder"/> 消费前的统一收集点
/// （SingleImplementation / TH-12，不复制收集逻辑）。
/// </summary>
/// <typeparam name="TState">折叠状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class AggregateNodeSource<TState, TVertex> : INodeSource<TState, TVertex>
{
    private readonly IReadOnlyList<INodeSource<TState, TVertex>> _sources;

    /// <param name="sources">待聚合的子源（非空；顺序即合并序）。</param>
    public AggregateNodeSource(IEnumerable<INodeSource<TState, TVertex>> sources)
    {
        var list = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
        // R-03：含 null 子源会在 CollectAsync 处退化为 NullReferenceException，改为构造期显式拒绝。
        if (list.Any(s => s is null))
            throw new ArgumentException("子源集合不可含 null。", nameof(sources));
        _sources = list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NodeItem>?> CollectAsync(
        TState state, FoldSignal<TVertex> signal, string stepKey, CancellationToken ct = default)
    {
        var result = new List<NodeItem>();
        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var items = await source.CollectAsync(state, signal, stepKey, ct).ConfigureAwait(false);
            // R-03：子源返回集合内的 null 元素同样在消费端炸；在收集边界统一剔除。
            if (items is { Count: > 0 }) result.AddRange(items.Where(i => i is not null));
        }
        return result.Count == 0 ? null : result;
    }
}

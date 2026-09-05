using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Node.Model;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.LoopFoldMethod.Node.Source;

/// <summary>
/// 节点源端口 —— 从当前折叠状态 + 驱动信号 + 步骤键收集一批节点（不落库，内存即时组装）。
/// 内容由消费域实现（提示词源 / 引用源 / 文档源等），框架只编排不解释；返回 null/空 = 该源无产出（零回归）。
/// 这是节点框架与折叠（<see cref="ISignalDrivenOperator{TState,TVertex}"/）的收集侧接缝。
/// </summary>
/// <typeparam name="TState">折叠状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface INodeSource<TState, TVertex>
{
    /// <summary>收集本步要注入的节点；返回 null/空表示无产出。</summary>
    /// <param name="state">当前状态点（只读）。</param>
    /// <param name="signal">当前驱动信号。</param>
    /// <param name="stepKey">本步步骤键。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<NodeItem>?> CollectAsync(
        TState state, FoldSignal<TVertex> signal, string stepKey, CancellationToken ct = default);
}

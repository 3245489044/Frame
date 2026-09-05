using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Calculus;
using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Adapters;

/// <summary>
/// 算子层复用适配器（方案 A 薄适配）：<see cref="IEvolver{TState}"/> → <see cref="IFoldOperator{TState,TVertex}"/>。
/// 演化即展开 —— evolver.Evolve 产出下一状态点，顶点经 vertexFactory 从下一状态合成（补齐 FoldVertex 算子结论）。
/// 纯端口包装，无引擎行为：循环推进/预算执法仍在 FoldEngine/SnapshotDrivenLoopEngine，EvolveUntilConverged 不充当折叠引擎。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型（由 vertexFactory 从下一状态合成）。</typeparam>
public sealed class EvolverFoldOperator<TState, TVertex>(
    IEvolver<TState> evolver,
    Func<TState, TVertex> vertexFactory)
    : IFoldOperator<TState, TVertex>
{
    /// <summary>演化即展开：next = evolver.Evolve(state)；顶点 = vertexFactory(next)。</summary>
    public Task<FoldStep<TState, TVertex>> Expand(TState state, FoldContext<TVertex> ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var next = evolver.Evolve(state);
        return Task.FromResult(new FoldStep<TState, TVertex>(next, vertexFactory(next)));
    }
}

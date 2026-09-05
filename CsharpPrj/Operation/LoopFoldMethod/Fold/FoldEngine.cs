using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Select;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 折叠引擎（unfold 宿主，唯一控制循环推进处）—— 以折叠（fold/unfold）为第一抽象，LLM-free。
/// 只做一件事：读当前状态点 → 交给外部算子展开出下一个状态点 → 追加到状态带 → 判定是否截断 → 继续或收敛。
/// 三个端口出让控制权：展开交给 <see cref="IFoldOperator{TState,TVertex}"/>、截断交给
/// <see cref="ICutoffPolicy{TState}"/>、折叠交给 <see cref="IFoldHeadProjector{TState}"/>；
/// 引擎本身只保留记账（坐标）、借出（控制权）、执法（截断/预算/收敛/空值守卫）。
/// <para>
/// 复用最底层 <see cref="SnapshotDrivenLoopEngine{TState,TResult}"/>（TH-12 单一实现，不另写 while）：
/// 折叠概念映射到既有循环 —— execute=算子展开、next=恒等转移+追加带、budgetTerminate=截断策略。
/// </para>
/// <para>设计文档组件树签名 <c>FoldEngine&lt;TState&gt;</c> 需同时承载算子结论（TVertex），故本实现为
/// <c>FoldEngine&lt;TState, TVertex&gt;</c>（两泛型）。</para>
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class FoldEngine<TState, TVertex>
{
    /// <summary>
    /// 以初始状态点 s₀ 运行折叠：展开（unfold）→ 截断（cutoff）→ 折叠（fold）→ 收敛（conclusion）。
    /// </summary>
    /// <param name="initialState">初始状态点 s₀。</param>
    /// <param name="op">外部算子（唯一外部驱动面，LLM/规则/人审皆实现）。</param>
    /// <param name="headProjector">折叠头投影：已展开前缀 → 当前判定点。</param>
    /// <param name="cutoff">截断策略（预算/轮次/收敛/取消 → FoldCut）。</param>
    /// <param name="budget">折叠预算（坐标上限）。</param>
    /// <param name="ct">取消令牌（探索终止载体）。</param>
    /// <param name="observer">可选观测（顶点/状态推进）。</param>
    /// <returns>折叠结论 <see cref="FoldResult{TState,TVertex}"/>。</returns>
    public async Task<FoldResult<TState, TVertex>> RunAsync(
        TState initialState,
        IFoldOperator<TState, TVertex> op,
        IFoldHeadProjector<TState> headProjector,
        ICutoffPolicy<TState> cutoff,
        FoldBudget budget,
        CancellationToken ct = default,
        IFoldObserver<TState, TVertex>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(headProjector);
        ArgumentNullException.ThrowIfNull(cutoff);
        ArgumentNullException.ThrowIfNull(budget);

        var trace = new FoldTrace<TState>();
        trace.Append(initialState);
        var vertices = new List<FoldVertex<TVertex>>();
        var coordinate = 0;
        var defensiveCut = FoldCut.Continue;
        var nextState = initialState;

        // 复用最底层快照驱动循环引擎（TH-12）：fold 概念映射到既有 while。
        var engine = new SnapshotDrivenLoopEngine<TState, FoldVertex<TVertex>>();
        var snapshot = await engine.RunAsync(
            initialState: initialState,
            execute: async (s, token) =>
            {
                // 借出控制权：交给外部算子展开 → 下一状态点 + 该步顶点。
                var ctx = new FoldContext<TVertex>(coordinate, budget, vertices);
                var step = await op.Expand(s, ctx, token).ConfigureAwait(false);

                // 空值守卫（外部边界校验）：不可信算子返回 null → 熔断执法（信任反转），停止展开。
                if (step is null)
                {
                    defensiveCut = FoldCut.TripTerminated;
                    return new FoldVertex<TVertex>(coordinate, default!);
                }

                nextState = step.Next;
                var vertex = new FoldVertex<TVertex>(coordinate, step.Vertex);
                vertices.Add(vertex);
                coordinate++;
                observer?.OnVertex(vertex);
                return vertex;
            },
            next: async (s, roundResult, token) =>
            {
                // 恒等转移 + 追加状态带到只增带（空值守卫后不再推进）。
                if (defensiveCut != FoldCut.Continue) return s;
                trace.Append(nextState);
                observer?.OnStateAdvanced(nextState);
                return nextState;
            },
            ct: ct,
            budgetTerminate: s =>
            {
                // 执法：空值守卫终值优先；否则经折叠头 + 截断策略裁决（不信任算子自停）。
                if (defensiveCut != FoldCut.Continue)
                    return FoldCutMapping.ToLoopDecision(defensiveCut);

                var head = headProjector.Head(trace.AsReadOnly());
                return FoldCutMapping.ToLoopDecision(cutoff.Decide(head, coordinate, budget));
            }).ConfigureAwait(false);

        // 收敛：外部终止（取消）→ Cancelled；否则引擎承载的预算终止值映射回 FoldCut；空值守卫兜底。
        var finalCut = snapshot.TerminatedByExternal
            ? FoldCut.Cancelled
            : FoldCutMapping.FromLoopDecision(snapshot.FinalDecision);
        if (defensiveCut != FoldCut.Continue && finalCut == FoldCut.Continue)
            finalCut = FoldCut.TripTerminated;

        var head = headProjector.Head(trace.AsReadOnly());
        return new FoldResult<TState, TVertex>(
            head,
            trace,
            vertices,
            coordinate,
            snapshot.TotalDuration,
            finalCut);
    }
}

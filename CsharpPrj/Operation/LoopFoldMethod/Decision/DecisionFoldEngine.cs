using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;
using Share.TaskStatusPool.Loops;
using FoldCutMapping = Operation.LoopFoldMethod.Select.FoldCutMapping;

namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 决策折叠引擎 —— 统一「决策式终止」与「折叠链收敛」的编排器（LLM-free，TH-12 复用最底层
/// <see cref="SnapshotDrivenLoopEngine{TState,TResult}"/>，不另写 while）。
/// 每步：① <see cref="IDeterministicTermination{TState}.Check"/> 确定性硬终止拦截（先于 skill，阈值即触发）
/// → ② 未命中则 <see cref="IDecisionSkill{TState,TVertex}.DecideAsync"/>（LLM 协程）产出 D′ / 终止
/// → ③ 无 D′ = 链终点收敛 → ④ 决策经 <see cref="DecisionOutcome"/> 统一承载（最终决策 = 折叠链标识符）。
/// 终止二分（AX.TerminationBipartition）：硬性终止引擎内（可判定，不外置）；取消经外部令牌（探索终止）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class DecisionFoldEngine<TState, TVertex>
{
    /// <summary>
    /// 以初始状态 + 初始信号运行决策折叠。
    /// </summary>
    /// <param name="initialState">初始状态点 s₀。</param>
    /// <param name="initialSignal">首条驱动信号（通常 ExternalInput，StepKey=链入口）。</param>
    /// <param name="deterministic">确定性硬终止（引擎内裁决，先于 skill）。</param>
    /// <param name="skill">LLM 软决策协程（产出 D′ 或终止）。</param>
    /// <param name="headProjector">折叠头投影（前缀 → 判定点）。</param>
    /// <param name="budget">折叠预算（坐标上限，软推进防护）。</param>
    /// <param name="ct">取消令牌（探索终止载体）。</param>
    /// <param name="observer">可选观测（顶点/状态推进）。</param>
    /// <returns>折叠结论 <see cref="FoldResult{TState,TVertex}"/>。</returns>
    public async Task<FoldResult<TState, TVertex>> RunAsync(
        TState initialState,
        FoldSignal<TVertex> initialSignal,
        IDeterministicTermination<TState> deterministic,
        IDecisionSkill<TState, TVertex> skill,
        IFoldHeadProjector<TState> headProjector,
        FoldBudget budget,
        CancellationToken ct = default,
        IFoldObserver<TState, TVertex>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(deterministic);
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(headProjector);
        ArgumentNullException.ThrowIfNull(budget);

        var trace = new FoldTrace<TState>();
        trace.Append(initialState);
        var vertices = new List<FoldVertex<TVertex>>();
        var coordinate = 0;
        var currentSignal = initialSignal;
        var finalSignal = TerminationSignal.Success;
        var stopped = false;

        // 复用最底层快照驱动循环引擎（TH-12）：决策 = 产出 D′/终止信号，映射到 execute/next/budgetTerminate。
        var engine = new SnapshotDrivenLoopEngine<TState, DecisionStep<TState, TVertex>>();
        var snapshot = await engine.RunAsync(
            initialState: initialState,
            execute: async (s, token) =>
            {
                // ① 确定性硬性终止拦截（引擎内，先于 LLM skill；阈值即触发，不外置）。
                var hard = deterministic.Check(s);
                if (hard is not null)
                {
                    stopped = true;
                    finalSignal = hard.Value;
                    return new DecisionStep<TState, TVertex>(s, default!, new DecisionOutcome.Terminate(hard.Value));
                }
                // ② LLM skill（协程）决策：产出下一步折叠链标识符 D′ / 链终点 / 终止。
                var ctx = new FoldContext<TVertex>(coordinate, budget, vertices);
                return await skill.DecideAsync(s, currentSignal, ctx, token).ConfigureAwait(false);
            },
            next: async (s, step, token) =>
            {
                if (stopped) return s;
                // Terminate（链终点/终止）：不推进坐标、不追加（停止步，对齐 resolver null 拦截语义——终止不产生推进步）。
                if (step.Outcome is DecisionOutcome.Terminate { Signal: var sig })
                {
                    stopped = true;
                    finalSignal = sig;
                    return s;
                }
                // Abort（显式中止，R7 fail-safe）：非成功终止，折叠收敛为熔断终止（Fail 原因经审计，见 P4 可观测）。
                if (step.Outcome is DecisionOutcome.Abort)
                {
                    stopped = true;
                    finalSignal = TerminationSignal.Halt;
                    return s;
                }
                // ContinueTo(D′)：推进坐标 + 顶点追加 + 状态带只增（InformationMonotonicity）+ 下一 StepCompleted 信号。
                coordinate++;
                var vertex = new FoldVertex<TVertex>(coordinate, step.Vertex);
                vertices.Add(vertex);
                observer?.OnVertex(vertex);
                trace.Append(step.Next);
                observer?.OnStateAdvanced(step.Next);
                if (step.Outcome is DecisionOutcome.ContinueTo { NextKey: var next })
                {
                    currentSignal = new FoldSignal<TVertex>(FoldSignalKind.StepCompleted, next, null, step.Vertex);
                }
                return step.Next;
            },
            ct: ct,
            budgetTerminate: s =>
            {
                // 预算终止（引擎内，可判定）：决策已到终态优先；否则坐标耗尽 → Timeout；继续。
                if (stopped)
                    return finalSignal == TerminationSignal.Success ? LoopDecision.Success : LoopDecision.TripTerminated;
                return budget.Exhausted(coordinate) ? LoopDecision.Timeout : LoopDecision.Continue;
            }).ConfigureAwait(false);

        // 收敛：外部终止（取消）→ Cancelled；决策终态 → 按终止信号映射 FoldCut；否则映射引擎承载的预算终止值。
        var head = headProjector.Head(trace.AsReadOnly());
        var finalCut = snapshot.TerminatedByExternal
            ? FoldCut.Cancelled
            : (stopped ? ToCut(finalSignal) : FoldCutMapping.FromLoopDecision(snapshot.FinalDecision));
        return new FoldResult<TState, TVertex>(
            head, trace, vertices, coordinate, snapshot.TotalDuration, finalCut);
    }

    /// <summary>终止信号 → FoldCut（保真：Success→收敛成功、Timeout→预算耗尽、TripTerminated/Halt→熔断终止）。</summary>
    private static FoldCut ToCut(TerminationSignal signal) => signal switch
    {
        TerminationSignal.Success => FoldCut.Success,
        TerminationSignal.Timeout => FoldCut.BudgetExhausted,
        _ => FoldCut.TripTerminated, // TripTerminated / Halt
    };
}

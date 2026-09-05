using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 信号驱动步进引擎 —— 唯一推进机制：消费一条信号（ExternalInput / StepCompleted）→ 解析（resolver）→
/// 展开一步（operator）→ 产出下一信号 → 链终点 / 预算 / 取消终止。
/// <para>
/// 复用 <see cref="FoldEngine{TState,TVertex}"/>（TH-12 单一实现，不做第二套 while）：本引擎仅把
/// 「状态点」换为「折叠状态 + 驱动信号」的内部运行态，把信号解析（resolver）注入步进执法处 ——
/// 链终点信号 → <see cref="FoldCut.Success"/>（run_to_end）；否则交 <see cref="ICutoffPolicy{TState}"/> 裁决。
/// </para>
/// <para>
/// LLM-free：信号解析与步进执法都在框架内；SK（技能 / 工具 / MCP）是 Agent 侧
/// <see cref="ISignalDrivenOperator{TState,TVertex}"/> 的实现，框架不感知。
/// </para>
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class SignalDrivenFoldEngine<TState, TVertex>
{
    /// <summary>
    /// 以初始信号运行信号驱动折叠：解析（resolver）→ 展开（operator）→ 链终点/截断 → 收敛（conclusion）。
    /// </summary>
    /// <param name="initialState">初始状态点 s₀。</param>
    /// <param name="initialSignal">首条驱动信号（通常 ExternalInput，StepKey=链入口）。</param>
    /// <param name="resolver">信号解析（LLM-free 链推进/终止判定）。</param>
    /// <param name="op">信号驱动算子（Agent 侧 SK 实现展开一步）。</param>
    /// <param name="headProjector">折叠头投影（前缀 → 判定点）。</param>
    /// <param name="cutoff">截断策略（预算/收敛/取消 → FoldCut）。</param>
    /// <param name="budget">折叠预算（坐标上限）。</param>
    /// <param name="ct">取消令牌（探索终止载体）。</param>
    /// <param name="observer">可选观测（顶点/状态推进）。</param>
    /// <returns>折叠结论 <see cref="FoldResult{TState,TVertex}"/>。</returns>
    public async Task<FoldResult<TState, TVertex>> RunAsync(
        TState initialState,
        FoldSignal<TVertex> initialSignal,
        ISignalResolver<TState, TVertex> resolver,
        ISignalDrivenOperator<TState, TVertex> op,
        IFoldHeadProjector<TState> headProjector,
        ICutoffPolicy<TState> cutoff,
        FoldBudget budget,
        CancellationToken ct = default,
        IFoldObserver<TState, TVertex>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(headProjector);
        ArgumentNullException.ThrowIfNull(cutoff);
        ArgumentNullException.ThrowIfNull(budget);

        // 内部运行态 = 折叠状态 + 驱动信号（经 FoldEngine 携带；信号驱动步进的"当前触发事件"）。
        var fold = new FoldEngine<RunState, TVertex>();
        var inner = await fold.RunAsync(
            initialState: new RunState(initialState, initialSignal),
            op: new SignalOperator(resolver, op),
            headProjector: new LastStateHeadProjector<RunState>(),
            cutoff: new SignalCutoffPolicy(cutoff, resolver),
            budget: budget,
            ct: ct,
            observer: observer is null ? null : new RunStateObserver(observer)).ConfigureAwait(false);

        // 折叠结论投影回用户 TState 带（内部 RunState 带 → TState 带 → 用户折叠头）。
        var trace = new FoldTrace<TState>(inner.Trace.AsReadOnly().Select(r => r.State));
        var head = headProjector.Head(trace.AsReadOnly());
        return new FoldResult<TState, TVertex>(
            head, trace, inner.Vertices, inner.Coordinate, inner.TotalDuration, inner.Cut);
    }

    /// <summary>内部运行态：折叠状态 + 当前驱动信号。</summary>
    private sealed record RunState(TState State, FoldSignal<TVertex> Signal);

    /// <summary>信号算子适配：解析器产出下一步指令 → 算子展开一步 → 产出 StepCompleted 信号。</summary>
    private sealed class SignalOperator : IFoldOperator<RunState, TVertex>
    {
        private readonly ISignalResolver<TState, TVertex> _resolver;
        private readonly ISignalDrivenOperator<TState, TVertex> _op;

        public SignalOperator(ISignalResolver<TState, TVertex> resolver, ISignalDrivenOperator<TState, TVertex> op)
        {
            _resolver = resolver;
            _op = op;
        }

        public async Task<FoldStep<RunState, TVertex>> Expand(RunState state, FoldContext<TVertex> ctx, CancellationToken ct)
        {
            // 链终点由步进执法（SignalCutoffPolicy）先行拦截；此处 null 属防御性兜底（理论上不可达）。
            var directive = _resolver.Resolve(state.Signal, state.State, ctx);
            if (directive is null)
                return new FoldStep<RunState, TVertex>(state, default!);

            var step = await _op.ExpandAsync(state.State, state.Signal, directive.ExecuteStepKey, ctx, ct).ConfigureAwait(false);

            // 本步产出 → 下一条 StepCompleted 信号（携带该步键 + 结论），交解析器驱动下一步。
            var nextSignal = new FoldSignal<TVertex>(
                FoldSignalKind.StepCompleted, step.StepKey, step.Vertex?.ToString(), step.Vertex);
            return new FoldStep<RunState, TVertex>(new RunState(step.Next, nextSignal), step.Vertex);
        }
    }

    /// <summary>步进执法截断策略：链终点（resolver 无下一步）→ 收敛成功（run_to_end）；否则交用户截断策略。</summary>
    private sealed class SignalCutoffPolicy : ICutoffPolicy<RunState>
    {
        private readonly ICutoffPolicy<TState> _inner;
        private readonly ISignalResolver<TState, TVertex> _resolver;

        public SignalCutoffPolicy(ICutoffPolicy<TState> inner, ISignalResolver<TState, TVertex> resolver)
        {
            _inner = inner;
            _resolver = resolver;
        }

        public FoldCut Decide(RunState head, int coordinate, FoldBudget budget)
        {
            // 链终点（无下一步）→ run_to_end 收敛成功（不信任算子自停，由框架执法）。
            var ctx = new FoldContext<TVertex>(coordinate, budget, Array.Empty<FoldVertex<TVertex>>());
            if (_resolver.Resolve(head.Signal, head.State, ctx) is null)
                return FoldCut.Success;
            return _inner.Decide(head.State, coordinate, budget);
        }
    }

    /// <summary>观测适配：RunState 带推进 → 用户 TState 状态推进；顶点原样透传。</summary>
    private sealed class RunStateObserver : IFoldObserver<RunState, TVertex>
    {
        private readonly IFoldObserver<TState, TVertex> _inner;

        public RunStateObserver(IFoldObserver<TState, TVertex> inner) => _inner = inner;

        public void OnVertex(FoldVertex<TVertex> vertex) => _inner.OnVertex(vertex);

        public void OnStateAdvanced(RunState state) => _inner.OnStateAdvanced(state.State);
    }
}

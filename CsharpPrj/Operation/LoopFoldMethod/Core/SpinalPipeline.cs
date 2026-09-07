using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Contracts;
using Operation.LoopFoldMethod.Routing;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Core;

/// <summary>
/// 脊柱管道默认实现 —— 泛型固定推进器。每轮沿 <see cref="SpinalStageOrder.Fixed"/> 顺序推进已注册作用点；
/// 首个 <see cref="ActionPoint{TState}.RunAsync"/> 的 handler 就此收敛（yield 终态）则停止推进该轮。
/// 底层 while 复用 <see cref="SnapshotDrivenLoopEngine{TState,TResult}"/>（TH-12 单一实现），
/// 预算终止经宿主 <c>budgetTerminate</c> 谓词（可回退到宿主状态承载的权威判定）；取消 = 探索终止（外置）。
/// 固定层独占：阶段顺序 / 有界 / 收敛；自由层经 Router 每级分发。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TResult">收敛结果类型（宿主收敛器产出）。</typeparam>
public sealed class SpinalPipeline<TState, TResult> : ISpinalPipeline<TState, TResult>
{
    private readonly IReadOnlyList<ActionPoint<TState>> _points;
    private readonly Func<TState, LoopDecision> _budgetTerminate;
    private readonly Func<TState, LoopDecision, int, TResult> _converge;

    /// <summary>
    /// 构造固定脊柱。
    /// </summary>
    /// <param name="routers">八作用点逐级路由（键 = 固定阶段；未注册的阶段在管道中空转，顺序仍按 <c>SpinalStageOrder.Fixed</c> 保持）。</param>
    /// <param name="budgetTerminate">预算终止谓词（引擎内，可判定；返回终态则 break）。默认 null = 不内置预算终止。</param>
    /// <param name="converge">收敛器：由最终状态 + 终态决策 + 已执行轮数产出 <typeparamref name="TResult"/>。</param>
    /// <param name="stageTimeout">单级作用点有界守卫超时（可选；0 = 仅协作取消）。</param>
    public SpinalPipeline(
        IReadOnlyDictionary<ActionStageId, IActionRouter<TState>> routers,
        Func<TState, LoopDecision>? budgetTerminate = null,
        Func<TState, LoopDecision, int, TResult>? converge = null,
        TimeSpan? stageTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(routers);
        _converge = converge
            ?? throw new ArgumentNullException(nameof(converge), "固定脊柱需宿主提供收敛器（converge）以产出 TResult。");
        _budgetTerminate = budgetTerminate ?? (_ => LoopDecision.Continue);

        // 按固定顺序把「已注册路由的作用点」排成推进序列（顺序不可改）。
        var points = new List<ActionPoint<TState>>();
        foreach (var stage in SpinalStageOrder.Fixed)
        {
            if (routers.TryGetValue(stage, out var router))
                points.Add(new ActionPoint<TState>(stage, router, stageTimeout));
        }
        _points = points;
    }

    /// <inheritdoc />
    public async Task<TResult> RunAsync(TState initialState, CancellationToken ct = default)
    {
        // 轮内某级 handler 收敛出的终态（TS/TO/TM）；None = 尚未收敛。
        var ended = LoopDecision.None;

        // 复用最底层快照驱动循环引擎（TH-12）：execute=一轮内沿固定顺序推进各作用点；next=状态带回写。
        var engine = new SnapshotDrivenLoopEngine<TState, TState>();
        var snapshot = await engine.RunAsync(
            initialState: initialState,
            execute: async (s, token) =>
            {
                var state = s;
                foreach (var point in _points)
                {
                    var call = new ActionCall<TState>(state, point.StageId);
                    var (outcome, yield) = await point.RunAsync(call, token).ConfigureAwait(false);
                    state = outcome.State;
                    // 固定顺序：某级收敛即不再推进后续级（终态短路口）。
                    if (yield is { } d && SpinalDecisions.IsTerminal(d))
                    {
                        ended = d;
                        break;
                    }
                }
                return state;
            },
            next: (_, roundState, _) => Task.FromResult(roundState),
            ct: ct,
            budgetTerminate: s => ended != LoopDecision.None ? ended : _budgetTerminate(s)).ConfigureAwait(false);

        // 收敛：轮内终态优先；否则引擎承载的预算终止值（非 None）；探索/取消与无终态则按 Continue 交收敛器定夺。
        var final = ended != LoopDecision.None ? ended
            : (snapshot.FinalDecision != LoopDecision.None ? snapshot.FinalDecision : LoopDecision.Continue);
        return _converge(snapshot.FinalState, final, snapshot.Rounds.Count);
    }
}

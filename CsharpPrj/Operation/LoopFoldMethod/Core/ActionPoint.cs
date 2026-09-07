using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading;
using Operation.LoopFoldMethod.Contracts;
using Operation.LoopFoldMethod.Routing;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Core;

/// <summary>
/// 作用点 —— 脊柱管道的一级固定壳：固定 <see cref="StageId"/> + 路由（自由，选哪个 handler）
/// + 有界守卫（固定：协作取消 / bounded grace）。框架对每级承诺：有界、单义、stall 可定位。
/// Handle 语义：有界守卫内 router.Select(call) → handler.HandleAsync(call) → 咨询 MayYield 是否收敛。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class ActionPoint<TState>
{
    /// <summary>本作用点阶段（固定枚举，不可改）。</summary>
    public ActionStageId StageId { get; }

    /// <summary>路由：自由层，每级决定选哪个 handler。</summary>
    public IActionRouter<TState> Router { get; }

    /// <summary>有界守卫超时（&gt;0 且非 Infinite 时对单次 HandleAsync 施以 bounded grace）；默认零 = 仅协作取消。</summary>
    public TimeSpan Timeout { get; }

    public ActionPoint(ActionStageId stageId, IActionRouter<TState> router, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        StageId = stageId;
        Router = router;
        Timeout = timeout ?? TimeSpan.Zero;
    }

    /// <summary>
    /// 在有界守卫内执行本作用点：Select → HandleAsync → MayYield。
    /// </summary>
    /// <returns>处理后状态 + 收敛终态（handler 就此收敛时非 null，供管道终止后续级）。</returns>
    public async Task<(HandlerOutcome<TState> Outcome, LoopDecision? Yield)> RunAsync(
        ActionCall<TState> call, CancellationToken ct)
    {
        // 路由：自由层选择 handler（窄接缝）。有路由但 Select 未命中 → fail-fast（不静默吞语义）。
        var handler = Router.Select(call)
            ?? throw new InvalidOperationException(
                $"作用点 {StageId} 已注册 Router 但未为调用匹配 handler（call.Stage={call.Stage}）。");

        // 有界守卫归一（A₀）：单次 HandleAsync 的有界执行上收 Modules.Threading.Bounded（BoundedExecution——
        // 协作取消 + 自身预算耗尽抛强类型 BoundedTimeoutException，外层取消一律上抛 OCE 不误报超时）。
        HandlerOutcome<TState> outcome;
        LoopDecision? yield;
        if (Timeout > TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            try
            {
                using var scope = BoundedExecution.CreateRoot($"action:{StageId}", external: ct);
                outcome = await scope.RunStepAsync("handle", t => handler.HandleAsync(call, t), Timeout).ConfigureAwait(false);
                // 咨询收敛：handler 就此收敛则给出终态决策（TS/TO/TM）。
                yield = handler.MayYield(out var decision) ? decision : null;
            }
            catch (BoundedTimeoutException)
            {
                // BoundedExecution 保证仅「自身预算耗尽」抛此强类型（外层取消已上抛 OCE）→ 确定性预算终止：
                // 以 Timeout 终态短路口停止后续级（SpinalDecisions.IsTerminal(Timeout)=true），状态保持进入前快照。
                return (new HandlerOutcome<TState>(call.State), LoopDecision.Timeout);
            }
        }
        else
        {
            // 未设预算：仅协作取消（响应外层 ct），不引入额外超时、零误杀。
            outcome = await handler.HandleAsync(call, ct).ConfigureAwait(false);
            yield = handler.MayYield(out var decision) ? decision : null;
        }
        return (outcome, yield);
    }
}

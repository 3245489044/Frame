using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Contracts;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Routing;

/// <summary>
/// 作用点路由 handler（自由逻辑的窄接缝）—— 固定层只认类型化 Port，不认具体自由逻辑。
/// 语义不进固定层：固定步仅调 <see cref="HandleAsync"/> 并咨询 <see cref="MayYield"/>；「选谁由 Router 决定」。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public interface IActionHandler<TState>
{
    /// <summary>
    /// 处理一次作用点调用，返回推进后状态。有界 / 取消 / 单义由调用方（<c>ActionPoint</c>）守卫承载，
    /// handler 只实现该级语义。
    /// </summary>
    Task<HandlerOutcome<TState>> HandleAsync(ActionCall<TState> call, CancellationToken ct);

    /// <summary>
    /// 是否就此收敛（管道是否该停止推进）。true 时输出 <c>LoopDecision</c> 终态（TS/TO/TM）供管道终止；
    /// false = 未收敛，管道沿固定顺序继续。权威：<c>Share.TaskStatusPool.Loops.LoopDecision</c>（AX.DecisionOutputSet）。
    /// </summary>
    bool MayYield(out LoopDecision decision);
}

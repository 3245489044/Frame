using System.Threading;
using System.Threading.Tasks;

namespace Operation.LoopFoldMethod.Core;

/// <summary>
/// 脊柱管道 —— 通用固定推进脊柱（顺序 · 有界 · 单义，框架独占）。
/// 内部按固定顺序推进八作用点（<c>SpinalStageOrder.Fixed</c>），底层 while 复用
/// <c>Share.TaskStatusPool.SnapshotDrivenLoopEngine</c>（TH-12 单一实现，不另写循环）；
/// 每级有界守卫 + 收敛检测由 <c>ActionPoint</c> 承载。Agent 只实现路由层 Router/Handler，不拥有引擎。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TResult">收敛结果类型（宿主定义）。</typeparam>
public interface ISpinalPipeline<TState, TResult>
{
    /// <summary>以初始状态运行固定脊柱直至收敛（终态 yield / 预算终止 / 取消）。</summary>
    Task<TResult> RunAsync(TState initialState, CancellationToken ct = default);
}

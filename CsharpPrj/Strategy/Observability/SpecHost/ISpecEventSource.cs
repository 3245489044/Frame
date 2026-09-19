using Strategy.Observability.Projection;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 观测事件源（领域侧实现 = <c>AgentObservability</c>；未注入 = 仅 <c>Port(…)</c> 根可用）。
/// <para>职责边界（U9/U11）：<b>读前排空（flush）与资源释放由实现承担</b>——内核与宿主不承担 Flush
/// （关键路径禁 Flush，仅用例出口例外，G-3），宿主只调用 <see cref="Read"/> 与 <see cref="Dispose"/>。</para>
/// </summary>
public interface ISpecEventSource : IDisposable
{
    /// <summary>是否可用（false = 未装配观测：<c>Event/Timeline/Aggregate</c> 三根须抛可定位异常，不得静默空集）。</summary>
    bool IsAvailable { get; }

    /// <summary>读取指定作用域的事件序（实现负责读前排空；无事件 → 空集）。</summary>
    IReadOnlyList<ObservationOutcome> Read(string scopeId);
}

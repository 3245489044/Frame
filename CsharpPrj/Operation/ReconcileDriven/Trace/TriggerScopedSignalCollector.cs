using Strategy.Observability.Bus;
using Strategy.Observability.Persistence;

namespace Operation.ReconcileDriven.Trace;

/// <summary>
/// 触发域信号收集器：作为观测总线的 <see cref="IObservabilitySink"/> 成员，把落在「当前活动触发域」
/// 上的语义观测事件同步收进该域的 <see cref="TriggerScope"/>。
/// <para>
/// 挂点纪律：本收集器注入 <see cref="ObservabilityBus"/> 的 Sink 复合（读源 = 同步、因果有序的语义观测），
/// <b>不</b>挂 EventSink（ur_events 异步批写 + at-least-once，实时对账会假红/假绿）。
/// </para>
/// <para>
/// 过滤：按 AsyncLocal 活动触发域 —— 无活动域 ⇒ 直通丢弃（零开销）；有活动域 ⇒ 记录（同逻辑链内的事件）。
/// 单线程 UI 触发天然命中；异步长任务经 AsyncLocal/上下文流动命中。
/// </para>
/// </summary>
public sealed class TriggerScopedSignalCollector : IObservabilitySink
{
    private static readonly AsyncLocal<TriggerScope?> Active = new();

    /// <summary>当前活动触发域（无则 null）。</summary>
    public static TriggerScope? Current => Active.Value;

    /// <summary>开启一个触发域：在其嵌套范围内 Push 的信号计入本域。返回句柄（Close/Dispose 摘域）。</summary>
    public TriggerScope Begin(string triggerId)
    {
        var scope = new TriggerScope(triggerId);
        Active.Value = scope;
        return scope;
    }

    /// <summary>同步接收一条已就绪事件：仅当存在活动触发域时入账。</summary>
    public void Push(PersistedEvent evt)
    {
        var scope = Active.Value;
        if (scope is null || evt?.Event is null) return;

        scope.Add(new SignalRecord(
            EventKind: evt.Event.EventKind,
            EventSeq: evt.Event.EventSeq,
            ScopeId: evt.ScopeId,
            Phase: evt.Event.Phase,
            Stage: evt.Event.Stage,
            TraceId: evt.TraceId,
            PayloadJson: evt.Event.PayloadJson));
    }

    /// <summary>非持久化 Sink：无排空动作。</summary>
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}

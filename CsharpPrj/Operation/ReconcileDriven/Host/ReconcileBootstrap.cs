using Operation.ReconcileDriven.Reconcile;
using Operation.ReconcileDriven.Trace;
using Share.EventBindingOrchestrator.Invocation;
using Share.EventBindingOrchestrator.Reconcile;
using Strategy.Observability.Bus;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账引导（组合根）：把对账能力接入框架触发链的唯一入口。
/// <para>
/// 接线要点（结构事实）：
/// 1) 目标 <see cref="IObservabilityBus"/> 的 Sink 复合须含 <see cref="TriggerScopedSignalCollector"/>
///    （<see cref="CreateCollector"/> 产出的实例，构造 Bus 时一并注入）——读源 = 同步语义观测，非 EventSink；
/// 2) <see cref="Install"/> 注入 <see cref="InvocationKernel.ReconcileHook"/>（所有函数调用唯一入口的包裹点）。
/// </para>
/// </summary>
public static class ReconcileBootstrap
{
    /// <summary>创建触发域信号收集器（须在构造观测 Bus 时作为 Sink 注入）。</summary>
    public static TriggerScopedSignalCollector CreateCollector() => new();

    /// <summary>
    /// 装配对账链：注入 <see cref="InvocationKernel.ReconcileHook"/>，返回钩子句柄（可空/不启用则直通）。
    /// </summary>
    /// <param name="bus">观测总线（须已含 collector 作为 Sink，否则信号账永远为空）。</param>
    /// <param name="collector">触发域信号收集器（与 Bus Sink 中为同一实例）。</param>
    /// <param name="options">运行时选项（总开关/终止等待窗）。</param>
    public static IEventReconcileHook Install(
        IObservabilityBus bus,
        TriggerScopedSignalCollector collector,
        ReconcileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(collector);

        var effective = options ?? new ReconcileOptions();
        var registry = new ReconcileContractRegistry();
        var stage = new ReconcileStage(bus, collector, new EventReconciler(), effective);
        var hook = new ReconcileHook(registry, stage, effective);
        InvocationKernel.ReconcileHook = hook;
        return hook;
    }

    /// <summary>卸载对账链（直通，零开销；显式退出可审计）。</summary>
    public static void Uninstall() => InvocationKernel.ReconcileHook = null;
}

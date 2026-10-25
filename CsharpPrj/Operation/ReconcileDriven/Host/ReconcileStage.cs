using System.Diagnostics;
using Operation.ReconcileDriven.Contracts;
using Operation.ReconcileDriven.Reconcile;
using Operation.ReconcileDriven.Trace;
using Strategy.Observability.Bus;
using Strategy.Observability.Entity;
using TConfig;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账阶段（触发域闭环执行体）：开账 → 执行 → 摘账 → 对账 → 裁决。
/// <para>
/// 唯一入口：由 <see cref="ReconcileHook"/> 在 <see cref="Share.EventBindingOrchestrator.Invocation.InvocationKernel"/>
/// 包裹一次函数调用时驱动；不改变既有「触发→执行→写回」路径。
/// </para>
/// </summary>
public sealed class ReconcileStage
{
    private readonly IObservabilityBus _bus;
    private readonly TriggerScopedSignalCollector _collector;
    private readonly IEventReconciler _reconciler;
    private readonly ReconcileOptions _options;

    public ReconcileStage(
        IObservabilityBus bus,
        TriggerScopedSignalCollector collector,
        IEventReconciler reconciler,
        ReconcileOptions? options = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _options = options ?? new ReconcileOptions();
    }

    /// <summary>
    /// 包裹一次触发执行：无契约/关闭 ⇒ 直通；否则开账执行、摘账对账、落账裁决（strict 失配抛 <see cref="EventReconcileException"/>）。
    /// </summary>
    public async Task<object?> RunAsync(ReconcileConfig? rawContract, string trigger, Func<Task<object?>> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);

        var contract = ReconcileContract.From(rawContract);
        if (!_options.Enabled || contract is null || contract.IsOff)
            return await invoke().ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var scope = _collector.Begin(trigger);
        object? output;
        try
        {
            output = await invoke().ConfigureAwait(false);
        }
        catch
        {
            scope.Close();   // 执行异常：摘账但不裁决（异常归因优先于对账）
            throw;
        }

        // 终止等待窗（异步长任务）：分钟级触发（如 composerSend）的执行体返回后，终止信号可能仍在途；
        // 窗口内轮询补齐，超窗即交由四判判 R3（未终止），不静默放行。
        await WaitForTerminalAsync(contract, scope, _options.TerminalWaitBudgetMs).ConfigureAwait(false);

        var trace = scope.Close();
        sw.Stop();

        var verdict = _reconciler.Reconcile(contract, trace);
        EmitVerdict(trigger, verdict, trace, sw.ElapsedMilliseconds);

        if (!verdict.Passed && contract.IsStrict)
            throw new EventReconcileException(trigger, verdict, trace);

        return output;
    }

    /// <summary>
    /// 终止等待窗：窗口 &gt;0 且契约声明了终止信号时，轮询等待终止信号出现或窗口耗尽。
    /// 窗口 ≤0 / 无终止声明 ⇒ 立即返回（同步假设，零开销）。
    /// </summary>
    private static async Task WaitForTerminalAsync(ReconcileContract contract, TriggerScope scope, int budgetMs)
    {
        if (budgetMs <= 0) return;

        // 终止信号集合单次计算（轮询内不再重建 ToList/Contains）。
        var terminals = contract.Signals
            .Where(s => s.Terminal && !string.IsNullOrWhiteSpace(s.Signal))
            .Select(s => s.Signal!)
            .ToHashSet(StringComparer.Ordinal);
        if (terminals.Count == 0) return;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budgetMs)
        {
            if (scope.Peek().Any(r => terminals.Contains(r.EventKind)))
                return;
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    /// <summary>落账裁决（ReconcilePassed/ReconcileFailed）；契约未注册时静默降级，不阻断主路径。</summary>
    private void EmitVerdict(string trigger, ReconcileVerdict verdict, SignalTrace trace, long elapsedMs)
    {
        var entity = new EntityId($"reconcile:{trigger}", "Reconcile", 1);
        var kind = verdict.Passed ? "ReconcilePassed" : "ReconcileFailed";
        try
        {
            _bus.Emit(entity, kind, b => b
                .WithMetric("signalCount", trace.Count)
                .WithMetric("elapsed_ms", elapsedMs)
                .Payload(verdict.Passed
                    ? new { trigger }
                    : new
                    {
                        trigger,
                        code = verdict.FailingCode,
                        missing = verdict.Missing,
                        outOfOrder = verdict.OutOfOrder,
                        terminalMissing = verdict.TerminalMissing,
                        scopeMismatch = verdict.ScopeMismatch,
                    }));
        }
        catch
        {
            // 观测面未就绪/契约未注册：不因落账失败而影响裁决结果（裁决本身已由返回值/异常承载）。
        }
    }
}

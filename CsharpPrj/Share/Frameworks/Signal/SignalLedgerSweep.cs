using System;
using System.Threading;
using System.Threading.Tasks;
using Share.Foundations.Diagnostics;
using Share.TaskStatusPool.TaskPool;

namespace Share.Signal;

/// <summary>信号账本清理面：Queued/租约 TTL → 终态；已终态超 TTL → 删除。</summary>
public class SignalLedgerSweep<TRecord> where TRecord : ITaskPoolItem
{
    private readonly SignalLedger<TRecord> _ledger;
    private readonly SignalObserver _observer;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly Func<bool> _enabled;

    public SignalLedgerSweep(SignalLedger<TRecord> ledger, SignalObserver observer,
        IDiagnosticsSink diagnostics, Func<bool>? enabled = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _enabled = enabled ?? (static () => true);
    }

    public async Task<SignalSweepResult> RunAsync(long? nowMs = null, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            _diagnostics.Report("SignalLedgerSweep_skipped", new { reason = "Signal.SweepEnabled=false" });
            return new SignalSweepResult(Array.Empty<SignalSweepChange>(), 0);
        }

        var result = await _ledger.SweepAsync(nowMs, ct).ConfigureAwait(false);
        foreach (var change in result.Changes)
            _observer.RecordState(change.Key.Scope, change.Key, change.State);
        return result;
    }
}

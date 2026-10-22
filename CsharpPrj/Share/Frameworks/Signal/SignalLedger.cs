using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.Foundations.Diagnostics;
using Share.TaskStatusPool.TaskPool;

namespace Share.Signal;

/// <summary>通用信号账本（载体经 ISignalLedgerCodec 适配）。</summary>
public class SignalLedger<TRecord> where TRecord : ITaskPoolItem
{
    private readonly ITaskPoolPersistence<TRecord> _store;
    private readonly ISignalLedgerCodec<TRecord> _codec;
    private readonly SignalLedgerOptions _options;
    private readonly IDiagnosticsSink _diagnostics;

    public SignalLedger(ITaskPoolPersistence<TRecord> store, ISignalLedgerCodec<TRecord> codec,
        SignalLedgerOptions options, IDiagnosticsSink diagnostics)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<SignalAdmitResult> AdmitAsync(SignalEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var key = KeyOf(envelope);
        var id = SignalId.New();
        var existing = await _store.GetAsync(LedgerId(key), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var record = SignalLedgerRecord.Parse(_codec.ReadPayload(existing));
            if (record is not null)
            {
                var state = record.State;
                if (state is SignalState.Completed or SignalState.Cancelled or SignalState.Expired or SignalState.DeadLetter)
                    return new SignalAdmitResult(false, true, record.Id, key, state, record.Attempt);
                if (state is not SignalState.Failed)
                    return new SignalAdmitResult(false, true, record.Id, key, state, record.Attempt);
                if (record.Attempt >= _options.MaxAttempts)
                {
                    Trace(record, SignalState.DeadLetter, record.Attempt, null);
                    await SaveAsync(existing, record.With(SignalState.DeadLetter, record.Attempt, null, 0), ct).ConfigureAwait(false);
                    _diagnostics.Report("SignalLedger_deadletter", new { key = key.Value, attempt = record.Attempt });
                    return new SignalAdmitResult(false, true, record.Id, key, SignalState.DeadLetter, record.Attempt);
                }
                var nextAttempt = record.Attempt + 1;
                Trace(record, SignalState.Accepted, nextAttempt, null);
                await SaveAsync(existing, record.With(SignalState.Accepted, nextAttempt, null, 0), ct).ConfigureAwait(false);
                return new SignalAdmitResult(true, false, record.Id, key, SignalState.Accepted, nextAttempt);
            }
            return new SignalAdmitResult(false, true, id, key, SignalState.Accepted, 0);
        }

        var item = NewItem(envelope, key, id, SignalState.Accepted, 0, null);
        await _store.EnqueueAsync(item, ct).ConfigureAwait(false);
        Trace(new SignalLedgerRecord(id, key, SignalState.Created, 0, null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), envelope, 0), SignalState.Accepted, 0, null);
        return new SignalAdmitResult(true, false, id, key, SignalState.Accepted, 0);
    }

    public async Task<SignalLedgerRecord?> GetAsync(SignalKey key, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(LedgerId(key), ct).ConfigureAwait(false);
        return item is null ? null : SignalLedgerRecord.Parse(_codec.ReadPayload(item));
    }

    public async Task<bool> MarkStateAsync(SignalKey key, SignalState state, int attempt,
        string? leaseId = null, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(LedgerId(key), ct).ConfigureAwait(false);
        if (item is null) return false;
        var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
        if (record is null) return false;
        if (!SignalStateMachine.CanTransition(record.State, state))
        {
            _diagnostics.Report("SignalLedger_illegal_transition", new
            {
                key = key.Value,
                from = SignalStateNames.Of(record.State),
                to = SignalStateNames.Of(state),
            });
            return false;
        }
        var leaseExpiresAt = state == SignalState.Dispatched
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _options.LeaseTtlMs : 0;
        Trace(record, state, attempt, leaseId);
        await SaveAsync(item, record.With(state, attempt, leaseId, leaseExpiresAt), ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> AckAsync(SignalKey key, int attempt, SignalOutcome outcome,
        string? leaseId = null, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(LedgerId(key), ct).ConfigureAwait(false);
        if (item is null) return false;
        var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
        if (record is null) return false;
        if (record.State is SignalState.Completed or SignalState.Cancelled or SignalState.Expired or SignalState.DeadLetter)
            return false;
        var terminal = SignalOutcomes.ToState(outcome);
        if (!SignalStateMachine.CanTransition(record.State, terminal))
        {
            _diagnostics.Report("SignalLedger_illegal_transition", new
            {
                key = key.Value,
                from = SignalStateNames.Of(record.State),
                to = SignalStateNames.Of(terminal),
            });
            return false;
        }
        Trace(record, terminal, Math.Max(attempt, record.Attempt), leaseId);
        await SaveAsync(item, record.With(terminal, Math.Max(attempt, record.Attempt), leaseId, 0), ct).ConfigureAwait(false);
        return true;
    }

    public async Task<SignalSweepResult> SweepAsync(long? nowMs = null, CancellationToken ct = default)
    {
        var now = nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changes = new List<SignalSweepChange>();
        var deleted = 0;
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, 500, ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
            if (record is null) continue;
            if (record.State == SignalState.Queued && IsExpired(record.UpdatedAt, _options.QueuedTtlMs, now))
            {
                if (await MarkStateAsync(record.Key, SignalState.Expired, record.Attempt, null, ct).ConfigureAwait(false))
                    changes.Add(new SignalSweepChange(record.Key, SignalState.Expired));
                continue;
            }
            if (record.State is SignalState.Dispatched or SignalState.Claimed or SignalState.Running
                && record.LeaseExpiresAt > 0 && now >= record.LeaseExpiresAt)
            {
                var target = record.State == SignalState.Dispatched ? SignalState.Expired : SignalState.Failed;
                if (await MarkStateAsync(record.Key, target, record.Attempt, null, ct).ConfigureAwait(false))
                    changes.Add(new SignalSweepChange(record.Key, target));
                continue;
            }
            if (SignalStateNames.IsTerminal(record.State) && IsExpired(record.UpdatedAt, _options.TerminalTtlMs, now))
            {
                if (await DeleteAsync(record.Key, ct).ConfigureAwait(false)) deleted++;
            }
        }
        if (changes.Count > 0 || deleted > 0)
            _diagnostics.Report("SignalLedger_sweep", new { changes = changes.Count, deleted });
        return new SignalSweepResult(changes, deleted);
    }


    public async Task<IReadOnlyList<PendingSignal>> ListPendingAsync(
        string? scope, string? coalesceKey, CancellationToken ct = default)
    {
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, 500, ct).ConfigureAwait(false);
        var result = new List<PendingSignal>();
        foreach (var item in items)
        {
            var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
            if (record is null) continue;
            if (record.State is not (SignalState.Accepted or SignalState.Queued or SignalState.Failed)) continue;
            if (!string.IsNullOrWhiteSpace(scope)
                && !string.Equals(record.Envelope.Scope, scope, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrWhiteSpace(coalesceKey)
                && !string.Equals(record.Envelope.EffectiveKey, coalesceKey, StringComparison.Ordinal)) continue;
            result.Add(new PendingSignal(_codec.TaskKey(item), record.Envelope));
        }
        return result;
    }

    public async Task<bool> HasInFlightAsync(SignalKey key, CancellationToken ct = default)
    {
        var record = await GetAsync(key, ct).ConfigureAwait(false);
        return record is not null && record.State is SignalState.Dispatched or SignalState.Claimed or SignalState.Running;
    }

    public async Task<IReadOnlyList<SignalLedgerRecord>> ListInFlightAsync(CancellationToken ct = default)
    {
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, 500, ct).ConfigureAwait(false);
        var result = new List<SignalLedgerRecord>();
        foreach (var item in items)
        {
            var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
            if (record is null) continue;
            if (record.State is SignalState.Dispatched or SignalState.Claimed or SignalState.Running)
                result.Add(record);
        }
        return result;
    }

    public Task<bool> DeleteAsync(SignalKey key, CancellationToken ct = default)
        => _store.DeleteAsync(LedgerId(key), ct);

    public static SignalKey KeyOf(SignalEnvelope envelope)
        => SignalKey.Of(envelope.Scope, envelope.Target, SignalKinds.Normalize(envelope.Kind));

    private TRecord NewItem(SignalEnvelope envelope, SignalKey key, SignalId id,
        SignalState state, int attempt, string? leaseId)
    {
        var record = new SignalLedgerRecord(id, key, state, attempt, leaseId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), envelope, 0);
        return _codec.Create(LedgerId(key), record.ToText(), envelope, envelope.Priority, _options.SignalExecutorId);
    }

    private Task SaveAsync(TRecord item, SignalLedgerRecord record, CancellationToken ct)
    {
        _codec.WritePayload(item, record.ToText());
        return _store.UpdatePayloadAsync(_codec.TaskKey(item), item, ct);
    }

    private static bool IsExpired(long updatedAt, long ttlMs, long now)
        => ttlMs > 0 && updatedAt > 0 && now - updatedAt >= ttlMs;

    private void Trace(SignalLedgerRecord record, SignalState to, int attempt, string? leaseId)
        => _diagnostics.Report("SignalLedger_transition", new
        {
            signalId = record.Id.Value,
            key = record.Key.Value,
            from = SignalStateNames.Of(record.State),
            to = SignalStateNames.Of(to),
            state = SignalStateNames.Of(to),
            attempt,
            leaseId = leaseId ?? "-",
        });

    public static string LedgerId(SignalKey key)
        => $"sigledger_{Slug(key.Scope)}_{Slug(key.Target)}_{Slug(key.Kind)}";

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "x";
        var chars = value.Where(char.IsLetterOrDigit).Take(32).ToArray();
        return chars.Length == 0 ? "x" : new string(chars);
    }
}


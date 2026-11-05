using System;
using System.Collections.Generic;
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
                // 单一判定：非 Failed（含全部终态）⇒ 去重命中；仅 Failed 进入重试预算。
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

            // 载荷损坏/未知状态：可见化后覆盖为新 Accepted（可重新进入扫描），不静默判重。
            _diagnostics.Report("SignalLedger_corrupt", new { key = key.Value, ledgerId = LedgerId(key) });
            var fresh = new SignalLedgerRecord(id, key, SignalState.Accepted, 0, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), envelope, 0);
            await SaveAsync(existing, fresh, ct).ConfigureAwait(false);
            return new SignalAdmitResult(true, false, id, key, SignalState.Accepted, 0);
        }

        // E11/Created 可达：先以 Created 落库（enqueue），再在同一接纳流程内保存为 Accepted。
        var created = new SignalLedgerRecord(id, key, SignalState.Created, 0, null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), envelope, 0);
        var item = NewItem(created, envelope);
        await _store.EnqueueAsync(item, ct).ConfigureAwait(false);
        Trace(created, SignalState.Accepted, 0, null);
        await SaveAsync(item, created.With(SignalState.Accepted, 0, null, 0), ct).ConfigureAwait(false);
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
        if (SignalStateNames.IsTerminal(record.State))
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
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, _options.ScanLimit, ct).ConfigureAwait(false);
        if (items.Count == _options.ScanLimit)
            _diagnostics.Report("SignalLedger_scan_truncated", new { operation = "sweep", limit = _options.ScanLimit });
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
            if (SignalStateNames.IsInFlight(record.State)
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
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, _options.ScanLimit, ct).ConfigureAwait(false);
        if (items.Count == _options.ScanLimit)
            _diagnostics.Report("SignalLedger_scan_truncated", new { operation = "list_pending", limit = _options.ScanLimit });
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
        return record is not null && SignalStateNames.IsInFlight(record.State);
    }

    public async Task<IReadOnlyList<SignalLedgerRecord>> ListInFlightAsync(CancellationToken ct = default)
    {
        var items = await _store.ListAsync(TaskExecutionStatus.NotStarted, _options.ScanLimit, ct).ConfigureAwait(false);
        if (items.Count == _options.ScanLimit)
            _diagnostics.Report("SignalLedger_scan_truncated", new { operation = "list_in_flight", limit = _options.ScanLimit });
        var result = new List<SignalLedgerRecord>();
        foreach (var item in items)
        {
            var record = SignalLedgerRecord.Parse(_codec.ReadPayload(item));
            if (record is null) continue;
            if (SignalStateNames.IsInFlight(record.State))
                result.Add(record);
        }
        return result;
    }

    public Task<bool> DeleteAsync(SignalKey key, CancellationToken ct = default)
        => _store.DeleteAsync(LedgerId(key), ct);

    public static SignalKey KeyOf(SignalEnvelope envelope)
        => SignalKey.Of(envelope.Scope, envelope.Target, SignalKinds.Normalize(envelope.Kind));

    private TRecord NewItem(SignalLedgerRecord record, SignalEnvelope envelope)
        => _codec.Create(LedgerId(record.Key), record.ToText(), envelope, envelope.Priority, _options.SignalExecutorId);

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

    /// <summary>
    /// 账本主键片段清洗：字母数字原样保留（常见 ≤32 位纯字母数字 key 的 LedgerId 保持不变，
    /// 避免已有持久化键失配）；其它字符编码为 <c>~</c> + 4 位十六进制（UTF-16 码元），
    /// 使 "task-a" 与 "taska" 不再撞键。超长时保留前 32 字符 + <c>~</c> + 稳定哈希（FNV-1a 64）。
    /// </summary>
    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "x";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else sb.Append('~').Append(((int)c).ToString("x4"));
        }

        var slug = sb.ToString();
        if (slug.Length == 0) return "x";
        return slug.Length <= 32 ? slug : slug.Substring(0, 32) + "~" + StableHash(value);
    }

    private static string StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash.ToString("x16");
    }
}


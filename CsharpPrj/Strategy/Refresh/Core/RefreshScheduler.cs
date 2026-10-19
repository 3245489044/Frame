using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 刷新调度引擎（L1，唯一实现）：Change / Heartbeat / Fallback 三车道 + 单拍互斥 +
    /// 无进展退避 + 停滞熔断 + 失败置回重试 + 空闲零推送。
    /// <para>Change 立即出；Heartbeat 只由可执行租约驱动并按 <see cref="SlotRefreshState.NextDelay"/> 退避；
    /// Fallback 仅在无 Change / Heartbeat 时出。零领域：推送 / 租约 / 停滞处置均由端口注入。</para>
    /// </summary>
    public sealed class RefreshScheduler : IDisposable
    {
        /// <summary>单次推送（scope 集合=本拍变更范围，null=全量；false = 未送达 → 置回下一拍重试）。</summary>
        public delegate Task<bool> PushOnce(IReadOnlyCollection<string>? scopes, CancellationToken ct);

        private readonly RefreshSchedulerOptions _options;
        private readonly PushOnce _pushOnce;
        private readonly IInFlightSource? _inFlightSource;
        private readonly IRefreshStore? _store;
        private readonly IStallPolicy _stallPolicy;
        private readonly Action<RefreshSlotDiagnostics>? _onDiagnostics;
        private readonly Func<TimeSpan, TimerCallback, IDisposable> _timerFactory;
        private readonly object _gate = new();
        private readonly HashSet<string> _dirtyKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _lowKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<IRefreshSource, Action<string?>> _handlers = new();
        private readonly Dictionary<string, DateTimeOffset> _nextDue = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _progressTokens = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _heartbeatNoChange = new(StringComparer.Ordinal);
        private readonly HashSet<string> _stalled = new(StringComparer.Ordinal);
        private IDisposable? _timer;
        private IDisposable? _pollTimer;
        private int _ticking;
        private bool _dirtyAll;
        private bool _lowAll;
        private bool? _lastResult;
        private long _pushes;
        private long _suppressedHeartbeats;

        public RefreshScheduler(
            RefreshSchedulerOptions options,
            PushOnce pushOnce,
            IInFlightSource? inFlightSource = null,
            IRefreshStore? store = null,
            IStallPolicy? stallPolicy = null,
            Action<RefreshSlotDiagnostics>? onDiagnostics = null,
            Func<TimeSpan, TimerCallback, IDisposable>? timerFactory = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.TickMs <= 0) throw new ArgumentOutOfRangeException(nameof(options));
            _pushOnce = pushOnce ?? throw new ArgumentNullException(nameof(pushOnce));
            _inFlightSource = inFlightSource;
            _store = store;
            _stallPolicy = stallPolicy ?? new LoggingStallPolicy();
            _onDiagnostics = onDiagnostics;
            _timerFactory = timerFactory
                ?? new Func<TimeSpan, TimerCallback, IDisposable>(
                    (interval, callback) => new Timer(callback, null, interval, interval));
        }

        public bool IsRunning { get { lock (_gate) return _timer is not null; } }

        public long PushCount => Interlocked.Read(ref _pushes);

        public long SuppressedHeartbeats => Interlocked.Read(ref _suppressedHeartbeats);

        /// <summary>起表（幂等）。</summary>
        public void Start()
        {
            lock (_gate)
                _timer ??= _timerFactory(TimeSpan.FromMilliseconds(_options.TickMs), OnTick);
        }

        /// <summary>停表（幂等；dirty / 租约状态保留，重启后可补推）。</summary>
        public void Stop()
        {
            lock (_gate) { _timer?.Dispose(); _timer = null; }
        }

        /// <summary>启用兜底轮询（Fallback 车道；低优先级，不抢占 Change / Heartbeat）。幂等。</summary>
        public void EnableIdlePolling(int intervalMs = RefreshContract.FallbackPollMs, string? scopeKey = null)
        {
            if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
            lock (_gate)
            {
                if (_pollTimer is not null) return;
                var key = string.IsNullOrWhiteSpace(scopeKey) ? null : scopeKey;
                _pollTimer = _timerFactory(TimeSpan.FromMilliseconds(intervalMs), _ => MarkDirty(key, RefreshPriority.Low));
            }
        }

        /// <summary>停兜底轮询（幂等）。</summary>
        public void DisableIdlePolling()
        {
            lock (_gate) { _pollTimer?.Dispose(); _pollTimer = null; }
        }

        /// <summary>登记刷新源（幂等；换实例即替换并停旧源）。返回 true = 实际登记。</summary>
        public bool RegisterSource(IRefreshSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            Action<string?> handler = scope => MarkDirty(scope, source.Priority);
            lock (_gate)
            {
                if (_handlers.ContainsKey(source)) return false;
                _handlers[source] = handler;
            }
            source.Changed += handler;
            source.Start();
            return true;
        }

        /// <summary>注销刷新源（按实例；幂等）。返回 true = 实际移除。</summary>
        public bool UnregisterSource(IRefreshSource source)
        {
            if (source is null) return false;
            Action<string?>? handler;
            lock (_gate)
            {
                if (!_handlers.TryGetValue(source, out handler)) return false;
                _handlers.Remove(source);
            }
            source.Changed -= handler;
            source.Stop();
            return true;
        }

        /// <summary>标记「全量有变化待刷新」（Change 车道）。</summary>
        public void MarkDirty()
        {
            lock (_gate)
            {
                _dirtyAll = true;
                ResetHeartbeatSlotsLocked(_options.UtcNow());
            }
        }

        /// <summary>标记「某范围有变化待刷新」（空/空白键等价于全量）。</summary>
        public void MarkDirty(string? scopeKey)
            => MarkDirty(scopeKey, RefreshPriority.High);

        /// <summary>按优先级标记变化（Low 不抢占 Change / Heartbeat）。</summary>
        public void MarkDirty(string? scopeKey, RefreshPriority priority)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
            {
                lock (_gate)
                {
                    if (priority == RefreshPriority.Low) _lowAll = true;
                    else
                    {
                        _dirtyAll = true;
                        ResetHeartbeatSlotsLocked(_options.UtcNow());
                    }
                }
                return;
            }

            var key = scopeKey!;
            lock (_gate)
            {
                if (priority == RefreshPriority.Low)
                {
                    _lowKeys.Add(key);
                    return;
                }
                _dirtyKeys.Add(key);
                _nextDue[key] = _options.UtcNow();
                _stalled.Remove(key);
                _heartbeatNoChange[key] = 0;
                ResetHeartbeatSlotsLocked(_options.UtcNow());
            }
        }

        private void ResetHeartbeatSlotsLocked(DateTimeOffset now)
        {
            foreach (var slot in _nextDue.Keys.ToArray())
            {
                _nextDue[slot] = now;
                _stalled.Remove(slot);
                _heartbeatNoChange[slot] = 0;
            }
            if (_store is null) return;
            foreach (var slot in _nextDue.Keys.ToArray())
                _store.ResetProgress(slot, now);
        }

        /// <summary>驱动一拍：Change → Heartbeat → Fallback 依次裁决；全空 ⇒ 零推送。</summary>
        public async Task TickAsync()
        {
            try
            {
                var now = _options.UtcNow();
                RefreshTrigger trigger;
                IReadOnlyCollection<string>? scopes;
                bool low;
                lock (_gate)
                {
                    if (_dirtyAll || _dirtyKeys.Count > 0)
                    {
                        low = false;
                        trigger = RefreshTrigger.Change;
                        scopes = _dirtyAll ? null : new List<string>(_dirtyKeys);
                        _dirtyAll = false;
                        _dirtyKeys.Clear();
                    }
                    else if (_lowAll || _lowKeys.Count > 0)
                    {
                        low = true;
                        trigger = RefreshTrigger.Fallback;
                        scopes = _lowAll ? null : new List<string>(_lowKeys);
                        _lowAll = false;
                        _lowKeys.Clear();
                    }
                    else
                    {
                        low = false;
                        trigger = RefreshTrigger.Heartbeat;
                        scopes = null;
                    }
                }

                if (trigger == RefreshTrigger.Heartbeat)
                {
                    var due = await PickDueHeartbeatsAsync(now).ConfigureAwait(false);
                    if (due.Count == 0) return;
                    scopes = due;
                }

                Interlocked.Increment(ref _pushes);
                var ok = await _pushOnce(scopes, CancellationToken.None).ConfigureAwait(false);
                ReportIfChanged(ok);

                if (!ok)
                {
                    lock (_gate)
                    {
                        if (low)
                        {
                            if (scopes is null) _lowAll = true;
                            else foreach (var s in scopes) _lowKeys.Add(s);
                        }
                        else if (trigger == RefreshTrigger.Heartbeat)
                        {
                            if (scopes is not null) foreach (var s in scopes) _nextDue[s] = now;
                        }
                        else
                        {
                            if (scopes is null) _dirtyAll = true;
                            else foreach (var s in scopes) _dirtyKeys.Add(s);
                        }
                    }
                    return;
                }

                if (trigger == RefreshTrigger.Heartbeat && scopes is not null)
                    ScheduleNext(now, scopes);

                ReportDiagnostics(trigger, scopes);
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        private async Task<IReadOnlyCollection<string>> PickDueHeartbeatsAsync(DateTimeOffset now)
        {
            var leases = await SnapshotAsync().ConfigureAwait(false);
            var present = new HashSet<string>(StringComparer.Ordinal);
            var due = new List<string>();
            foreach (var lease in leases)
            {
                if (!IsExecutable(lease, now)) continue;
                present.Add(lease.Key);

                bool hasToken;
                string? lastToken;
                lock (_gate) hasToken = _progressTokens.TryGetValue(lease.Key, out lastToken);
                if (!hasToken)
                {
                    lock (_gate)
                    {
                        _progressTokens[lease.Key] = lease.ProgressToken;
                        _nextDue[lease.Key] = now;
                        _heartbeatNoChange[lease.Key] = 0;
                    }
                }
                else if (!string.Equals(lastToken, lease.ProgressToken, StringComparison.Ordinal))
                {
                    lock (_gate)
                    {
                        _progressTokens[lease.Key] = lease.ProgressToken;
                        _nextDue[lease.Key] = now;
                        _stalled.Remove(lease.Key);
                        _heartbeatNoChange[lease.Key] = 0;
                    }
                    _store?.ResetProgress(lease.Key, now);
                }

                DateTimeOffset next;
                bool stalled;
                lock (_gate)
                {
                    if (!_nextDue.TryGetValue(lease.Key, out next))
                    {
                        next = now;
                        _nextDue[lease.Key] = next;
                    }
                    stalled = _stalled.Contains(lease.Key);
                }
                if (now < next) continue;

                var state = _store?.GetState(lease.Key) ?? SlotRefreshState.Empty;
                if (state.LastOutcome == RefreshOutcome.Changed)
                {
                    lock (_gate)
                    {
                        _stalled.Remove(lease.Key);
                        _heartbeatNoChange[lease.Key] = 0;
                    }
                    stalled = false;
                }
                if (stalled) continue;

                int noChange;
                lock (_gate) _heartbeatNoChange.TryGetValue(lease.Key, out noChange);
                if (ShouldStall(lease, state, noChange, now))
                {
                    if (MarkStalled(lease.Key))
                    {
                        Interlocked.Increment(ref _suppressedHeartbeats);
                        _ = _stallPolicy.OnStalledAsync(lease.Key, leases, state, CancellationToken.None);
                    }
                    continue;
                }

                due.Add(lease.Key);
            }

            lock (_gate)
            {
                foreach (var key in _nextDue.Keys.Where(k => !present.Contains(k)).ToArray())
                {
                    _nextDue.Remove(key);
                    _progressTokens.Remove(key);
                    _heartbeatNoChange.Remove(key);
                    _stalled.Remove(key);
                }
            }
            return due;
        }

        private static bool IsExecutable(InFlightLease lease, DateTimeOffset now)
        {
            if (!lease.Active) return false;
            if (!string.Equals(lease.Kind, InFlightLease.ExecutableKind, StringComparison.OrdinalIgnoreCase)) return false;
            if (lease.Deadline is { } deadline && deadline <= now) return false;
            return true;
        }

        private bool ShouldStall(InFlightLease lease, SlotRefreshState state, int heartbeatNoChange, DateTimeOffset now)
        {
            if (lease.Deadline is { } deadline && deadline <= now) return true;
            if (now - lease.LastHeartbeatAt > TimeSpan.FromMilliseconds(_options.StallWindowMs)) return true;
            return Math.Max(state.ConsecutiveNoChange, heartbeatNoChange) >= _options.StallAfterNoChange;
        }

        private bool MarkStalled(string key)
        {
            lock (_gate) return _stalled.Add(key);
        }

        private void ScheduleNext(DateTimeOffset now, IReadOnlyCollection<string> keys)
        {
            lock (_gate)
            {
                foreach (var key in keys)
                {
                    _heartbeatNoChange.TryGetValue(key, out var pushed);
                    pushed += 1;
                    _heartbeatNoChange[key] = pushed;

                    var state = _store?.GetState(key) ?? SlotRefreshState.Empty;
                    var exponent = Math.Min(Math.Max(state.ConsecutiveNoChange, pushed),
                        RefreshContract.ProgressBackoffMaxExponent);
                    var delayMs = (long)_options.TickMs * (1L << exponent);
                    if (delayMs > _options.MaxHeartbeatMs) delayMs = _options.MaxHeartbeatMs;
                    _nextDue[key] = now + TimeSpan.FromMilliseconds(delayMs);
                }
            }
        }

        private async Task<IReadOnlyList<InFlightLease>> SnapshotAsync()
        {
            if (_inFlightSource is null) return Array.Empty<InFlightLease>();
            try
            {
                return await _inFlightSource.SnapshotAsync(CancellationToken.None).ConfigureAwait(false)
                       ?? Array.Empty<InFlightLease>();
            }
            catch
            {
                return Array.Empty<InFlightLease>();
            }
        }

        /// <summary>直接推送一次刷新请求（不经过车道裁决；返回 false = 未送达）。</summary>
        public async Task<bool> PushOnceAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _pushes);
            var ok = await _pushOnce(null, ct).ConfigureAwait(false);
            ReportIfChanged(ok);
            if (!ok) MarkDirty();
            return ok;
        }

        private void ReportDiagnostics(RefreshTrigger trigger, IReadOnlyCollection<string>? scopes)
        {
            if (_onDiagnostics is null || scopes is null) return;
            foreach (var slot in scopes)
            {
                var state = _store?.GetState(slot) ?? SlotRefreshState.Empty;
                _onDiagnostics(new RefreshSlotDiagnostics(
                    slot, trigger, state.LastOutcome, state.LastDeliveredVersion, state.LastProgressToken,
                    state.ConsecutiveNoChange, state.ConsecutiveDeliveryFailures, state.NextDelay,
                    0, state.LastChangedAt, state.NoProgressSince,
                    PushCount, 0, SuppressedHeartbeats));
            }
        }

        private void OnTick(object? state)
        {
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            _ = TickAsync();
        }

        private void ReportIfChanged(bool ok)
        {
            lock (_gate)
            {
                if (_lastResult == ok) return;
                _lastResult = ok;
            }
        }

        /// <summary>停表 + 停兜底轮询 + 注销全部刷新源。</summary>
        public void Dispose()
        {
            IRefreshSource[] sources;
            Action<string?>[] handlers;
            lock (_gate)
            {
                sources = new List<IRefreshSource>(_handlers.Keys).ToArray();
                handlers = new List<Action<string?>>(_handlers.Values).ToArray();
                _handlers.Clear();
            }
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].Changed -= handlers[i];
                sources[i].Stop();
            }
            DisableIdlePolling();
            Stop();
        }
    }
}



using System;
using System.Collections.Generic;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// <see cref="IRefreshStore"/> 的线程安全默认实现：每 slot 记版本水位 / 指纹 / 连续无进展 / 退避 / 最近结论。
    /// <para>全部读写经 lock 串行；<see cref="GetState"/> 返回不可变快照，调用方不得持有内部状态。</para>
    /// </summary>
    public sealed class InMemoryRefreshStore : IRefreshStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, SlotState> _slots = new(StringComparer.Ordinal);
        private readonly RefreshSchedulerOptions _options;

        public InMemoryRefreshStore(RefreshSchedulerOptions? options = null)
            => _options = options ?? new RefreshSchedulerOptions();

        private sealed class SlotState
        {
            public long Baseline = long.MinValue; // 未见过的 slot：任何版本皆不判过期
            public long Next;
            public string LastFingerprint = string.Empty;
            public DateTimeOffset LastChangedAt = DateTimeOffset.MinValue;
            public DateTimeOffset LastAttemptAt = DateTimeOffset.MinValue;
            public int ConsecutiveNoChange;
            public int ConsecutiveDeliveryFailures;
            public RefreshOutcome LastOutcome = RefreshOutcome.Unchanged;
            public string? LastProgressToken;
            public DateTimeOffset? NoProgressSince;

            public SlotRefreshState Snapshot(TimeSpan nextDelay)
                => new(
                    Baseline,
                    LastFingerprint,
                    LastChangedAt,
                    LastAttemptAt,
                    ConsecutiveNoChange,
                    ConsecutiveDeliveryFailures,
                    LastOutcome,
                    nextDelay,
                    LastProgressToken,
                    NoProgressSince);
        }

        private SlotState GetOrAdd(string slotKey)
        {
            if (!_slots.TryGetValue(slotKey, out var state))
            {
                state = new SlotState();
                _slots[slotKey] = state;
            }
            return state;
        }

        private TimeSpan NextDelay(int noChange)
        {
            var exponent = Math.Min(Math.Max(0, noChange), RefreshContract.ProgressBackoffMaxExponent);
            var ms = (long)_options.TickMs * (1L << exponent);
            if (ms > _options.MaxHeartbeatMs) ms = _options.MaxHeartbeatMs;
            return TimeSpan.FromMilliseconds(ms);
        }

        /// <inheritdoc />
        public long NextVersion(string slotKey)
        {
            if (string.IsNullOrWhiteSpace(slotKey))
                throw new ArgumentException("slotKey 不得为空", nameof(slotKey));
            lock (_gate)
            {
                var state = GetOrAdd(slotKey);
                state.Next += 1;
                return state.Next;
            }
        }

        /// <inheritdoc />
        public AcceptDecision Decide(string slotKey, long version, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(slotKey))
                throw new ArgumentException("slotKey 不得为空", nameof(slotKey));
            lock (_gate)
            {
                if (!_slots.TryGetValue(slotKey, out var state))
                    return AcceptDecision.Apply; // 首写：无 baseline、无 last
                if (version < state.Baseline)
                    return AcceptDecision.Stale; // 版本过期即丢
                if (state.LastFingerprint.Length > 0
                    && string.Equals(state.LastFingerprint, fingerprint, StringComparison.Ordinal))
                    return AcceptDecision.Skip; // 指纹未变
                return AcceptDecision.Apply;
            }
        }

        /// <inheritdoc />
        public void Commit(string slotKey, long version, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(slotKey))
                throw new ArgumentException("slotKey 不得为空", nameof(slotKey));
            lock (_gate)
            {
                var state = GetOrAdd(slotKey);
                if (version < state.Baseline)
                    return; // 不后退：过期快照不得回写覆盖更新的
                state.Baseline = version;
                state.LastFingerprint = fingerprint;
            }
        }

        /// <inheritdoc />
        public SlotRefreshState GetState(string slotKey)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) return SlotRefreshState.Empty;
            lock (_gate)
            {
                if (!_slots.TryGetValue(slotKey, out var state)) return SlotRefreshState.Empty;
                return state.Snapshot(NextDelay(state.ConsecutiveNoChange));
            }
        }

        /// <inheritdoc />
        public void RecordOutcome(
            string slotKey,
            RefreshOutcome outcome,
            string fingerprint,
            string? progressToken,
            DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) return;
            lock (_gate)
            {
                var state = GetOrAdd(slotKey);
                state.LastAttemptAt = at;
                state.LastOutcome = outcome;
                state.LastFingerprint = fingerprint;

                var progressed = !string.IsNullOrWhiteSpace(progressToken)
                                 && !string.Equals(state.LastProgressToken, progressToken, StringComparison.Ordinal);
                if (progressed) state.LastProgressToken = progressToken;

                if (outcome == RefreshOutcome.Changed || progressed)
                {
                    state.ConsecutiveNoChange = 0;
                    state.ConsecutiveDeliveryFailures = 0;
                    state.NoProgressSince = null;
                    if (outcome == RefreshOutcome.Changed) state.LastChangedAt = at;
                    return;
                }

                state.ConsecutiveNoChange += 1;
                if (outcome == RefreshOutcome.Failed)
                    state.ConsecutiveDeliveryFailures += 1;
                state.NoProgressSince ??= at;
            }
        }

        /// <inheritdoc />
        public void ResetProgress(string slotKey, DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) return;
            lock (_gate)
            {
                var state = GetOrAdd(slotKey);
                state.ConsecutiveNoChange = 0;
                state.ConsecutiveDeliveryFailures = 0;
                state.NoProgressSince = null;
                state.LastAttemptAt = at;
            }
        }
    }
}



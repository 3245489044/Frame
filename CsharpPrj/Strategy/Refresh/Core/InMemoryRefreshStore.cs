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
            public long LastRecordedVersion = long.MinValue; // 已记录的最近一次尝试版本（乱序守卫）
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
            => RecordOutcomeCore(slotKey, null, outcome, fingerprint, progressToken, at);

        /// <inheritdoc />
        public void RecordOutcome(
            string slotKey,
            long version,
            RefreshOutcome outcome,
            string fingerprint,
            string? progressToken,
            DateTimeOffset at)
            => RecordOutcomeCore(slotKey, version, outcome, fingerprint, progressToken, at);

        /// <summary>
        /// 记录一次尝试的反馈状态（连续无进展 / 失败 / 最近结论）。
        /// <para><b>不变量</b>：<see cref="SlotState.LastFingerprint"/> 只能与 <see cref="SlotState.Baseline"/>
        /// 在 <see cref="Commit"/> 内以同一把锁一次推进，本方法<b>永不</b>覆写指纹——否则「跳过 / 过期 / 未送达」
        /// 会把基线之上的指纹写进状态，令真实内容变化后被 <see cref="Decide"/> 永久判 Skip。</para>
        /// <para><b>版本守卫</b>：带版本时，早于 baseline 或早于已记录版本的乱序结论直接丢弃，不拉回统计水位。</para>
        /// </summary>
        private void RecordOutcomeCore(
            string slotKey,
            long? version,
            RefreshOutcome outcome,
            string fingerprint,
            string? progressToken,
            DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) return;
            lock (_gate)
            {
                var state = GetOrAdd(slotKey);
                if (version is long recordedVersion)
                {
                    if (recordedVersion < state.Baseline || recordedVersion < state.LastRecordedVersion) return;
                    state.LastRecordedVersion = recordedVersion;
                }

                state.LastAttemptAt = at;
                state.LastOutcome = outcome;
                // LastFingerprint 与 Baseline 的推进只属于 Commit（单一写入点、单锁原子）。

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



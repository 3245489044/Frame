using System;
using System.Collections.Generic;

namespace Share.RefreshFramework
{
    /// <summary>
    /// <see cref="IRefreshStore"/> 的线程安全默认实现：每 slot 记 Baseline（版本水位）与 LastFingerprint（去重指纹），
    /// Next 记该 slot 的单调版本分配器。全部读写经 lock 串行。
    /// </summary>
    public sealed class InMemoryRefreshStore : IRefreshStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, SlotState> _slots = new(StringComparer.Ordinal);

        private sealed class SlotState
        {
            public long Baseline = long.MinValue; // 未见过的 slot：任何版本皆不判过期
            public string? LastFingerprint;
            public long Next;
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
                if (state.LastFingerprint is not null && string.Equals(state.LastFingerprint, fingerprint, StringComparison.Ordinal))
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
    }
}

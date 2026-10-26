using System;
using System.Collections.Generic;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 一拍一票（L1 聚拢点）：每 slot 的「当前拍」由 traceId 唯一标识，同一拍只允许渲染一次。
    /// <para>语义：<c>TryBegin(slot, traceId)==true</c> ⇔ 该 traceId 首次占用该槽（或未携带 traceId）。</para>
    /// <para>重复投递（同一 traceId 在页面回投/消息桥重入中出现第二次）⇒ 返回 false，调用方须在**渲染前**早退，
    /// 不产生第二次全量重投影。页面侧配合：一次推送只回投一次（trace 消费后清空）。</para>
    /// <para>零领域、零 IO；仅持有「槽 → 最近一次拍号」，不做时效淘汰（拍号单调，覆盖即失效）。</para>
    /// </summary>
    public sealed class RefreshBeatOwnership
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _lastTrace = new(StringComparer.Ordinal);
        private long _suppressed;

        /// <summary>
        /// 占用本拍。返回 false = 同一槽的同一 traceId 已经占用（重复拍，须在渲染前早退）。
        /// traceId 为空时不做拍级去重（调用方未提供身份，不得据此吞掉刷新）。
        /// </summary>
        public bool TryBegin(string slotKey, string? traceId)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) throw new ArgumentException("slotKey 不得为空", nameof(slotKey));
            if (string.IsNullOrWhiteSpace(traceId)) return true;

            lock (_gate)
            {
                if (_lastTrace.TryGetValue(slotKey, out var previous)
                    && string.Equals(previous, traceId, StringComparison.Ordinal))
                {
                    _suppressed++;
                    return false;
                }

                _lastTrace[slotKey] = traceId!;
                return true;
            }
        }

        /// <summary>本槽当前拍号（未占用 ⇒ null）。</summary>
        public string? CurrentTrace(string slotKey)
        {
            if (string.IsNullOrWhiteSpace(slotKey)) return null;
            lock (_gate) return _lastTrace.TryGetValue(slotKey, out var trace) ? trace : null;
        }

        /// <summary>累计被拍级去重拦下的重复拍数（结构事实，供诊断复算）。</summary>
        public long SuppressedCount
        {
            get { lock (_gate) return _suppressed; }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Common.Diagnostics
{
    /// <summary>
    /// 崩溃/卡死瞬间的现场环（聚拢点）：最近 N 条日志 + 最近 K 个活动相位。
    /// 单写者追加、崩溃时只读；挂 LogRepository.LogWritten，健康路径零额外分配。
    /// </summary>
    public static class EvidenceRing
    {
        public const int LogCapacity = 200;

        public const int PhaseCapacity = 16;

        private static readonly ConcurrentQueue<string> _logs = new();

        private static readonly ConcurrentQueue<EvidencePhase> _phases = new();

        private static int _hooked;

        /// <summary>幂等挂载（订阅日志广播，供崩溃瞬间取日志环）。</summary>
        public static void Attach()
        {
            if (Interlocked.Exchange(ref _hooked, 1) == 1) return;
            LogRepository.LogWritten += OnLogWritten;
        }

        private static void OnLogWritten(LogEntry entry)
        {
            _logs.Enqueue($"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] [{entry.Module}] {entry.Message}");
            Trim(_logs, LogCapacity);
        }

        /// <summary>慢路径相位心跳（零日志）：健康时仅一次引用入队。</summary>
        public static void MarkPhase(string phase, string? resource = null, string? owner = null)
        {
            _phases.Enqueue(new EvidencePhase(DateTime.UtcNow, phase, resource, owner));
            Trim(_phases, PhaseCapacity);
        }

        public static EvidenceSnapshot Snapshot()
        {
            return new EvidenceSnapshot(_logs.ToArray(), _phases.ToArray());
        }

        public static void Reset()
        {
            while (_logs.TryDequeue(out _)) { }
            while (_phases.TryDequeue(out _)) { }
        }

        private static void Trim<T>(ConcurrentQueue<T> queue, int capacity)
        {
            while (queue.Count > capacity && queue.TryDequeue(out _)) { }
        }
    }

    /// <summary>活动相位采样（慢路径自报告）。</summary>
    public sealed record EvidencePhase(DateTime AtUtc, string Phase, string? Resource, string? Owner);

    /// <summary>崩溃现场快照（日志环 + 相位环）。</summary>
    public sealed record EvidenceSnapshot(string[] Logs, EvidencePhase[] Phases)
    {
        public string? LastPhase => Phases.Length == 0 ? null : Phases[Phases.Length - 1].Phase;

        public static EvidenceSnapshot Empty { get; } = new(Array.Empty<string>(), Array.Empty<EvidencePhase>());
    }
}

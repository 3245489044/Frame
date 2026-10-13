using System;
using System.Collections.Generic;

namespace Common.Diagnostics
{
    /// <summary>崩溃/异常分类（取证侧；覆盖可捕获终止类型）。</summary>
    public enum CrashKind
    {
        ThreadException,

        AppDomainUnhandled,

        UnobservedTask,

        FirstChance,

        Halt
    }

    /// <summary>
    /// 结构化崩溃证据（扩展 ErrorRecord）：异常 + 栈 + 线程 + 活动相位 + 日志环快照 + 状态快照。
    /// 序列化为 NDJSON 单行，独立 crash/ 目录，禁写折叠日志面。
    /// </summary>
    public sealed class CrashRecord
    {
        public string CrashId { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public CrashKind Kind { get; set; }

        public int ProcessId { get; set; } = Environment.ProcessId;

        public int ThreadId { get; set; } = Environment.CurrentManagedThreadId;

        public string? Phase { get; set; }

        public string? Source { get; set; }

        public string? ExceptionType { get; set; }

        public string? Message { get; set; }

        public string? StackTrace { get; set; }

        public string[] LastLogs { get; set; } = Array.Empty<string>();

        public EvidencePhase[] RecentPhases { get; set; } = Array.Empty<EvidencePhase>();

        /// <summary>异常瞬间的状态快照（保持当前状态，不推进）。</summary>
        public Dictionary<string, object?>? State { get; set; }
    }
}

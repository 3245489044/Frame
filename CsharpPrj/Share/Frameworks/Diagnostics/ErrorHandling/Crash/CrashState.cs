using System;

namespace Common.Diagnostics
{
    /// <summary>进程遗嘱状态：异常/崩溃发生瞬间的当前活动相位快照（保持当前状态）。</summary>
    public sealed class CrashState
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        public int ProcessId { get; set; } = Environment.ProcessId;

        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public string? Phase { get; set; }

        public string? Detail { get; set; }
    }
}

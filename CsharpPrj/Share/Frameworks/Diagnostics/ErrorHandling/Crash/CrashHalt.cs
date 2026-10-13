using System;

namespace Common.Diagnostics
{
    /// <summary>
    /// 异常停驻信号（聚拢点）：异常即中断后续步骤并保持当前状态。
    /// 供执行循环查询 IsHalted 以在异常后停止推进（而非继续下一步），现场由 CrashStore 留存。
    /// </summary>
    public static class CrashHalt
    {
        private static volatile bool _halted;

        private static CrashRecord? _last;

        public static bool IsHalted => _halted;

        public static CrashRecord? LastCrash => _last;

        public static event Action<CrashRecord>? Halted;

        public static void Signal(CrashRecord record)
        {
            _last = record;
            _halted = true;

            var handler = Halted;
            if (handler == null) return;

            try
            {
                handler(record);
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"CrashHalt 回调异常: {ex.Message}");
            }
        }

        public static void Clear()
        {
            _halted = false;
            _last = null;
        }
    }
}

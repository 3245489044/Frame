using System;

namespace Common.Diagnostics
{
    /// <summary>
    /// 取证链自身的降级出口：可见但不反噬主链（catch 合法出口，避免静默吞并）。
    /// </summary>
    internal static class CrashDiag
    {
        public static void Degrade(string message)
        {
            Console.WriteLine("[CrashDiag] " + message);
        }
    }
}

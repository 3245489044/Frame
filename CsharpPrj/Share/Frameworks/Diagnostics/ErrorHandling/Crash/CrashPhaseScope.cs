using System;

namespace Common.Diagnostics
{
    /// <summary>
    /// 崩溃相位作用域（聚拢点）：进入即写 <c>crash-state.json</c> 的 Phase/Detail（<see cref="LastWillWriter.Mark"/>）。
    /// <para>用途：原生崩溃不可捕获，只能靠"最后进入的相位"自证；热路径须只在状态变化时调用。</para>
    /// </summary>
    public static class CrashPhaseScope
    {
        /// <summary>进入相位（返回可释放句柄，便于 using 表达作用域；Dispose 不回退相位）。</summary>
        /// <param name="phase">受控相位名（executor / gui-script / paint / …）。</param>
        /// <param name="detail">可选细节（命令序号、宿主 id 等）。</param>
        public static IDisposable Enter(string phase, string? detail = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phase);
            LastWillWriter.Mark(phase, detail);
            return Handle.Instance;
        }

        private sealed class Handle : IDisposable
        {
            public static readonly Handle Instance = new();

            public void Dispose()
            {
                // 相位保持（崩溃时的现场即"最后进入的相位"）；不在此回退，避免恢复逻辑误读。
            }
        }
    }
}

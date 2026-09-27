using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 惰性单次引导门（once-gate，线程安全）：双检锁 + 共享进行中 Task —— 并发调用复用同一自举任务、防重复启动；
    /// 显式 <see cref="Clear"/> 使失败/不可用态可重试。消除「惰性自举脚手架」在多处的复制。
    /// <para>通用件：零领域类型（启动工厂与取消令牌由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="T">引导结果类型。</typeparam>
    public sealed class OnceGate<T>
    {
        private readonly object _gate = new();
        private Task<T>? _task;

        /// <summary>取或启动一次引导。已缓存（含进行中）→ 返回缓存任务（共享并发调用）；否则用 <paramref name="start"/> 启动并缓存。</summary>
        public Task<T> GetOrStart(Func<CancellationToken, Task<T>> start, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(start);
            lock (_gate)
            {
                if (_task is { } cached) return cached;
                _task = start(ct);
                return _task;
            }
        }

        /// <summary>清空缓存（失败态/结果不可用），下次 <see cref="GetOrStart"/> 可重试。幂等。</summary>
        public void Clear()
        {
            lock (_gate) { _task = null; }
        }
    }
}

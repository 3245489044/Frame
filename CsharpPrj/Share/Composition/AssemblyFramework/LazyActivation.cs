using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 惰性激活门（once-gate 合成）—— 前置快路径（已就绪直接返回）+ 并发共享单次激活 + 失败/空结果不缓存状态
    /// （清门可重试）+ 激活后联动回调。把「未装配 → 惰性自举一次」的过程逻辑从宿主多处形似实现收敛为单一承载。
    /// <para>通用件：零领域类型（探测 / 激活 / 回调由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="T">激活结果类型（引用类型）。</typeparam>
    public sealed class LazyActivation<T> where T : class
    {
        private readonly Func<T?> _probe;
        private readonly Func<CancellationToken, Task<T?>> _activate;
        private readonly Action<T>? _onActivated;
        private readonly OnceGate<T?> _gate = new();

        /// <param name="probe">前置快路径：已就绪返回实例，否则 null。</param>
        /// <param name="activate">单次激活（null = 瞬时失败，不缓存、下次可重试）。</param>
        /// <param name="onActivated">激活成功后的联动（如登记槽位）。</param>
        public LazyActivation(Func<T?> probe, Func<CancellationToken, Task<T?>> activate, Action<T>? onActivated = null)
        {
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _activate = activate ?? throw new ArgumentNullException(nameof(activate));
            _onActivated = onActivated;
        }

        /// <summary>取或激活一次。已就绪 → 直接返回；并发调用复用同一进行中任务；空结果 → 清门可重试。</summary>
        public Task<T?> GetOrActivateAsync(CancellationToken ct = default)
        {
            if (_probe() is { } ready) return Task.FromResult<T?>(ready);

            return _gate.GetOrStart(async innerCt =>
            {
                // 锁内二次就位校验（对「恰于本调用并发装配」作幂等确认）。
                if (_probe() is { } again) return again;

                var value = await _activate(innerCt).ConfigureAwait(false);
                if (value is null)
                {
                    // 瞬时失败不缓存失败态：下次调用重试。
                    _gate.Clear();
                    return null;
                }
                _onActivated?.Invoke(value);
                return value;
            }, ct);
        }
    }
}

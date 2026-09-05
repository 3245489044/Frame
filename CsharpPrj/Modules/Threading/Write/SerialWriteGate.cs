using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading
{
    /// <summary>
    /// 串行写门（原 Operation.HierarchyMethod.Center.WriteCoordinator 的通用归属）：把并发写者对确定性核心的写串行化。
    /// 用 <see cref="AsyncLock"/>（await 式互斥 + 重入自证）串行单写 + <see cref="BoundedExecution"/> 有界等待——
    /// 并发写者排队、互不互锁；锁被持卡死（预算内未取得写门）→ 抛 <see cref="BoundedTimeoutException"/>（Kind=Store）自证写对象
    /// （不再无日志卡死）。可选：构造时经 <see cref="SingleWriterRegistry"/> 注册为某写目标的唯一确定性写路径
    /// （运行时 AssertSingleWriter 双保险）。
    /// </summary>
    public sealed class SerialWriteGate : IDisposable
    {
        private readonly AsyncLock _gate;
        private readonly TimeSpan? _defaultTimeout;

        public SerialWriteGate(TimeSpan? defaultTimeout = null,
            string? singleWriterName = null, string? singleWriteTarget = null)
        {
            _defaultTimeout = defaultTimeout;
            _gate = new AsyncLock(singleWriteTarget ?? "SerialWriteGate");
            if (singleWriterName is not null && singleWriteTarget is not null)
                SingleWriterRegistry.AssertSingleWriter(singleWriteTarget, singleWriterName);
        }

        /// <summary>串行单写 + 有界等待执行一次写入。默认预算见构造 <paramref name="defaultTimeout"/>；显式 <paramref name="timeout"/> 覆盖。</summary>
        public async Task<T> RunWriteAsync<T>(
            string target, Func<CancellationToken, Task<T>> write,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentNullException(nameof(target));
            ArgumentNullException.ThrowIfNull(write);

            var effective = timeout ?? _defaultTimeout;
            using var scope = BoundedExecution.CreateRoot("write", effective, ct);
            return await scope.RunStoreAsync(target, async t =>
            {
                // 写门有界获取；锁被持卡死 → t 取消 → Bounded 转 Store 超时自证
                using var lease = await _gate.AcquireAsync(t).ConfigureAwait(false);
                return await write(t).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>串行单写 + 有界等待执行一次写入（无返回值体）。</summary>
        public async Task RunWriteAsync(
            string target, Func<CancellationToken, Task> write,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (write is null) throw new ArgumentNullException(nameof(write));
            await RunWriteAsync<bool>(target, async t => { await write(t).ConfigureAwait(false); return true; }, timeout, ct)
                .ConfigureAwait(false);
        }

        public void Dispose() { }
    }
}

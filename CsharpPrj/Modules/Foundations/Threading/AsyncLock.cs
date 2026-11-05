using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading
{
    /// <summary>
    /// 异步互斥原语（架构优化：为单一确定性写/临界区提供 await 式互斥 + 重入死锁自证）。
    /// 以 SemaphoreSlim(1,1) 承载；AsyncLocal 检测**同一逻辑执行链内的重入获取**（否则将自死锁），fail-fast 抛异常。
    /// 可与 <see cref="BoundedExecution"/> 组合得到有界获取（预算窗口内取不到锁 → 窗口取消 → Bounded 转强类型超时自证）。
    /// 本模块零外部 NuGet 依赖（System.Threading.Channels 为 .NET inbox），AsyncLock 为内建原语。
    /// </summary>
    public sealed class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly AsyncLocal<bool> _holding = new();
        private readonly string _name;

        public AsyncLock(string? name = null)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "AsyncLock" : name;
        }

        public string Name => _name;

        /// <summary>是否已被某持有者占用（诊断用；CurrentCount==0）。</summary>
        public bool IsHeld => _semaphore.CurrentCount == 0;

        /// <summary>异步获取锁（可取消）。返回句柄，Dispose 释放。同一逻辑链内重入获取 → 抛 <see cref="InvalidOperationException"/>（死锁自证）。</summary>
        public async ValueTask<AsyncLockHandle> AcquireAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfReentrant();
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            _holding.Value = true;
            return new AsyncLockHandle(this);
        }

        /// <summary>带超时的异步获取锁；超时未取得 → 抛 <see cref="TimeoutException"/>。</summary>
        public async Task<AsyncLockHandle> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            ThrowIfReentrant();
            var acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (!acquired)
                throw new TimeoutException($"AsyncLock '{_name}' acquire timed out after {timeout}");
            _holding.Value = true;
            return new AsyncLockHandle(this);
        }

        private void ThrowIfReentrant()
        {
            if (_holding.Value)
                throw new InvalidOperationException(
                    $"AsyncLock '{_name}' reentrant acquire in same logical execution detected (would deadlock). Use separate lock or restructure.");
        }

        internal void ReleaseCore()
        {
            _holding.Value = false;
            _semaphore.Release();
        }
    }

    /// <summary>AsyncLock 的租约句柄：Dispose 释放锁。</summary>
    public readonly struct AsyncLockHandle : IDisposable
    {
        private readonly AsyncLock? _owner;

        internal AsyncLockHandle(AsyncLock owner) => _owner = owner;

        public void Dispose() => _owner?.ReleaseCore();
    }
}

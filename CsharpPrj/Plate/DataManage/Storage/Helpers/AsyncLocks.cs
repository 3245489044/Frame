#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataManage.Storage.Helpers
{
    public sealed class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new LockReleaser(_semaphore);
        }

        private sealed class LockReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private bool _disposed;

            public LockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

            public void Dispose()
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _disposed = true;
                }
            }
        }
    }

    public sealed class AsyncReaderWriterLock
    {
        private readonly SemaphoreSlim _readerSemaphore = new(int.MaxValue, int.MaxValue);
        private readonly SemaphoreSlim _writerSemaphore = new(1, 1);
        private int _readerCount;

        public async Task<IDisposable> ReadLockAsync(CancellationToken cancellationToken = default)
        {
            await _writerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            
            if (Interlocked.Increment(ref _readerCount) == 1)
            {
                await _readerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            
            _writerSemaphore.Release();
            return new ReadLockReleaser(this);
        }

        public async Task<IDisposable> WriteLockAsync(CancellationToken cancellationToken = default)
        {
            await _writerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _readerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new WriteLockReleaser(this);
        }

        private void ReleaseRead()
        {
            if (Interlocked.Decrement(ref _readerCount) == 0)
            {
                _readerSemaphore.Release();
            }
        }

        private void ReleaseWrite()
        {
            _readerSemaphore.Release();
            _writerSemaphore.Release();
        }

        private sealed class ReadLockReleaser : IDisposable
        {
            private readonly AsyncReaderWriterLock _lock;
            private bool _disposed;

            public ReadLockReleaser(AsyncReaderWriterLock @lock) => _lock = @lock;

            public void Dispose()
            {
                if (!_disposed)
                {
                    _lock.ReleaseRead();
                    _disposed = true;
                }
            }
        }

        private sealed class WriteLockReleaser : IDisposable
        {
            private readonly AsyncReaderWriterLock _lock;
            private bool _disposed;

            public WriteLockReleaser(AsyncReaderWriterLock @lock) => _lock = @lock;

            public void Dispose()
            {
                if (!_disposed)
                {
                    _lock.ReleaseWrite();
                    _disposed = true;
                }
            }
        }
    }
}

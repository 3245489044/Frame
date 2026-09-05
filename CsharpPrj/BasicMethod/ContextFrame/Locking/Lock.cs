
using System.Runtime.CompilerServices;
using System.Threading;

namespace BasicMethod.ContextFrame.Locking
{
    public ref struct MonitorLockScope
    {
        private object? _locker;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MonitorLockScope(object locker)
        {
            _locker = locker ?? throw new ArgumentNullException(nameof(locker));
            Monitor.Enter(_locker);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_locker != null && Monitor.IsEntered(_locker))
            {
                Monitor.Exit(_locker);
            }
            _locker = null;
        }
    }

    public ref struct MonitorTryLockScope
    {
        private object? _locker;
        private bool _lockTaken;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MonitorTryLockScope(object locker, int millisecondsTimeout)
        {
            _locker = locker ?? throw new ArgumentNullException(nameof(locker));
            _lockTaken = Monitor.TryEnter(locker, millisecondsTimeout);
        }

        public readonly bool LockAcquired => _lockTaken;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_lockTaken && _locker != null && Monitor.IsEntered(_locker))
            {
                Monitor.Exit(_locker);
            }
            _locker = null;
        }
    }

    public ref struct ReadLockScope
    {
        private readonly ReaderWriterLockSlim _rwLock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadLockScope(ReaderWriterLockSlim rwLock)
        {
            _rwLock = rwLock ?? throw new ArgumentNullException(nameof(rwLock));
            _rwLock.EnterReadLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_rwLock != null)
            {
                try
                {
                    if (_rwLock.IsReadLockHeld)
                        _rwLock.ExitReadLock();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public ref struct WriteLockScope
    {
        private readonly ReaderWriterLockSlim _rwLock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WriteLockScope(ReaderWriterLockSlim rwLock)
        {
            _rwLock = rwLock ?? throw new ArgumentNullException(nameof(rwLock));
            _rwLock.EnterWriteLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_rwLock != null)
            {
                try
                {
                    if (_rwLock.IsWriteLockHeld)
                        _rwLock.ExitWriteLock();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public ref struct UpgradeableReadLockScope
    {
        private readonly ReaderWriterLockSlim _rwLock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UpgradeableReadLockScope(ReaderWriterLockSlim rwLock)
        {
            _rwLock = rwLock ?? throw new ArgumentNullException(nameof(rwLock));
            _rwLock.EnterUpgradeableReadLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_rwLock != null)
            {
                try
                {
                    if (_rwLock.IsUpgradeableReadLockHeld)
                        _rwLock.ExitUpgradeableReadLock();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public static class Lock
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MonitorLockScope Monitor(object locker) => new(locker);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MonitorTryLockScope TryMonitor(object locker, int millisecondsTimeout) => new(locker, millisecondsTimeout);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadLockScope Read(ReaderWriterLockSlim rwLock) => new(rwLock);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WriteLockScope Write(ReaderWriterLockSlim rwLock) => new(rwLock);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpgradeableReadLockScope UpgradeableRead(ReaderWriterLockSlim rwLock) => new(rwLock);
    }
}

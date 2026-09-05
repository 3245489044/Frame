using ECS.Core;
using Modules.ComponentData.Lifecycle;
using BasicMethod.ContextFrame.Locking;

namespace Modules.ComponentData.Enhanced
{
    public class EcsContext : IDisposable
    {
        private const int DefaultJoinTimeoutSeconds = 5;

        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _signal = new(false);
        private Thread? _bgThread;
        private bool _disposed;
        private readonly ReaderWriterLockSlim _worldLock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly ComponentLifecycleManager _lifecycleManager;

        public EcsWorld World { get; }
        public RelationManager Relations { get; }
        public IndexManager Indexes { get; }
        public NotificationCenter Notifications { get; }

        public EcsContext(
            TConfig.ContextFrame.Abstractions.IECSStorage ecsStorage,
            TConfig.ContextFrame.Abstractions.IStorageRegistry storageRegistry,
            bool enableBgUpdate = false,
            SynchronizationContext? uiCtx = null)
        {
            World = new EcsWorld(ecsStorage, storageRegistry);
            var eventSource = new EntityManagerEventSource(World.EntityManager);
            _lifecycleManager = new(eventSource, uiCtx, disposeHandlersOnShutdown: true);
            Relations = new(World.EntityManager, _worldLock, _lifecycleManager);
            Indexes = new(World.EntityManager, _worldLock, _lifecycleManager);
            Notifications = new(World.EntityManager, _lifecycleManager, uiCtx);
            if (enableBgUpdate) StartBackgroundUpdate();
        }

        public void StartBackgroundUpdate(int intervalMs = 0)
        {
            using (Locks.Write(_worldLock))
            {
                if (_bgThread != null)
                    throw new InvalidOperationException("Background thread already running.");

                _bgThread = new Thread(() =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            using (Locks.Write(_worldLock))
                            {
                                World.Update(0);
                                Indexes.ProcessDeferredRemovals();
                            }
                        }
                        catch { }
                        _signal.WaitOne(intervalMs == 0 ? Timeout.Infinite : intervalMs);
                    }
                })
                { IsBackground = true, Name = "EcsContext_UpdateLoop" };

                _bgThread.Start();
            }
        }

        public void SignalUpdate() => _signal.Set();

        public void StopBackgroundUpdate()
        {
            Thread? threadToJoin = null;
            using (Locks.Write(_worldLock))
            {
                if (_bgThread == null) return;
                _cts.Cancel();
                _signal.Set();
                threadToJoin = _bgThread;
                _bgThread = null;
            }
            if (threadToJoin != null && threadToJoin.IsAlive && !threadToJoin.Join(TimeSpan.FromSeconds(DefaultJoinTimeoutSeconds)))
                throw new InvalidOperationException("Background thread failed to stop within timeout.");
        }

        public void RegisterSystem(SystemBase system)
        {
            using (Locks.Write(_worldLock))
                World.SystemManager.RegisterSystem(system);
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed)) return;

            _cts.Cancel();
            _signal.Set();

            Thread? threadToJoin = null;
            lock (_bgThread ?? (object)_cts)
            {
                threadToJoin = _bgThread;
                _bgThread = null;
            }

            if (threadToJoin != null && threadToJoin.IsAlive)
            {
                if (!threadToJoin.Join(TimeSpan.FromSeconds(DefaultJoinTimeoutSeconds)))
                    System.Diagnostics.Debug.WriteLine("[EcsContext] Background thread failed to stop within timeout.");
            }

            using (Locks.Write(_worldLock))
            {
                if (_disposed) return;
                Relations.Dispose();
                Indexes.Dispose();
                Notifications.Dispose();
                World.Dispose();
                _lifecycleManager.Dispose();
                _cts.Dispose();
                _signal.Dispose();
                _worldLock.Dispose();
                Volatile.Write(ref _disposed, true);
            }
        }
    }
}

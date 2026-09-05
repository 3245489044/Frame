using Modules.Common.Cloning;
using TConfig.ContextFrame.Abstractions;
using DictionaryAlgorithm;
using ECS.Core;
using GraphFoundation.Abstractions;
using GraphFoundation.Core.Commands;
using GraphFoundation.Topology;
using Locking;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lock = Locking.Lock;
#nullable enable



#region ToolRegion
#region CommonUtilities

namespace Common
{

    using System;
    using System.Collections.Concurrent;
    using System.Threading;

    #region 锁管理

    public interface ILockManager
    {
        IDisposable EnterReadLock();

        IDisposable EnterWriteLock();

        IDisposable EnterUpgradeableReadLock();
    }

    public class ReaderWriterLockManager : ILockManager
    {
        private readonly ReaderWriterLockSlim _lock;

        public ReaderWriterLockManager(LockRecursionPolicy recursionPolicy = LockRecursionPolicy.SupportsRecursion)
        {
            _lock = new ReaderWriterLockSlim(recursionPolicy);
        }

        public IDisposable EnterReadLock()
        {
            return new LockDisposable(_lock, LockType.Read);
        }

        public IDisposable EnterWriteLock()
        {
            return new LockDisposable(_lock, LockType.Write);
        }

        public IDisposable EnterUpgradeableReadLock()
        {
            return new LockDisposable(_lock, LockType.UpgradeableRead);
        }

        private enum LockType { Read, Write, UpgradeableRead }

        private struct LockDisposable : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            private readonly LockType _type;

            public LockDisposable(ReaderWriterLockSlim lockObj, LockType type)
            {
                _lock = lockObj;
                _type = type;

                if (_type == LockType.Read) _lock.EnterReadLock();
                else if (_type == LockType.Write) _lock.EnterWriteLock();
                else _lock.EnterUpgradeableReadLock();
            }

            public void Dispose()
            {
                if (_type == LockType.Read) _lock.ExitReadLock();
                else if (_type == LockType.Write) _lock.ExitWriteLock();
                else _lock.ExitUpgradeableReadLock();
            }
        }
    }

    public class MonitorLockManager : ILockManager
    {
        private readonly object _lockObject = new();

        public IDisposable EnterReadLock()
        {
            System.Threading.Monitor.Enter(_lockObject);
            return new MonitorDisposable(_lockObject);
        }

        public IDisposable EnterWriteLock()
        {
            System.Threading.Monitor.Enter(_lockObject);
            return new MonitorDisposable(_lockObject);
        }

        public IDisposable EnterUpgradeableReadLock()
        {
            System.Threading.Monitor.Enter(_lockObject);
            return new MonitorDisposable(_lockObject);
        }

        private struct MonitorDisposable : IDisposable
        {
            private readonly object _lockObject;

            public MonitorDisposable(object lockObject)
            {
                _lockObject = lockObject;
            }

            public void Dispose()
            {
                System.Threading.Monitor.Exit(_lockObject);
            }
        }
    }

    #endregion

    #region 缓存服务

    public interface ICacheService
    {
        T? Get<T>(string key);

        void Set<T>(string key, T value);

        bool TryGet<T>(string key, out T? value);

        bool Remove(string key);

        void Clear();

        int Count { get; }
    }

    public class InMemoryCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, object> _cache = new();

        public T? Get<T>(string key)
        {
            return _cache.TryGetValue(key, out var value) ? (T?)value : default;
        }

        public void Set<T>(string key, T value)
        {
            _cache[key] = value!;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var obj))
            {
                value = (T?)obj;
                return true;
            }
            value = default;
            return false;
        }

        public bool Remove(string key)
        {
            return _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public int Count => _cache.Count;
    }

    public interface ICacheKeyGenerator
    {
        string GenerateKey(params object[] parts);
    }

    public class DefaultCacheKeyGenerator : ICacheKeyGenerator
    {
        public string GenerateKey(params object[] parts)
        {
            return string.Join(":", parts.Select(p => p?.ToString() ?? "null"));
        }
    }

    #endregion

    #region 工厂管理器


    public interface ICommandFactory<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        ICommand CreateAddNodeCommand(TKey key, TNodeData data, string? symbol = null);
        ICommand CreateRemoveNodeCommand(TKey key);
        ICommand CreateAddEdgeCommand(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0);
        ICommand CreateRemoveEdgeCommand(string edgeId);
    }

    public class DefaultCommandFactory<TKey, TNodeData, TEdgeData> : ICommandFactory<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public ICommand CreateAddNodeCommand(TKey key, TNodeData data, string? symbol = null)
            => new AddNodeCommand<TKey, TNodeData, TEdgeData>(key, data, symbol);
        public ICommand CreateRemoveNodeCommand(TKey key)
            => new RemoveNodeCommand<TKey, TNodeData, TEdgeData>(key);
        public ICommand CreateAddEdgeCommand(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0)
            => new AddEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId, source, target, isDirected, data, weight);
        public ICommand CreateRemoveEdgeCommand(string edgeId)
            => new RemoveEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId);
    }

    public interface IUniqueIdGenerator
    {
        string Generate(string baseId, int chainIndex, Func<string, bool> isNameTaken);
        void Update(string pattern, int maxAttempts);
    }

    public class DefaultUniqueIdGenerator : IUniqueIdGenerator
    {
        private string _pattern = "{0}_chain{1}_dup{2}";
        private int _maxAttempts = 100;

        public DefaultUniqueIdGenerator() { }
        public DefaultUniqueIdGenerator(string pattern, int maxAttempts)
        {
            _pattern = pattern;
            _maxAttempts = maxAttempts;
        }

        public string Generate(string baseId, int chainIndex, Func<string, bool> isNameTaken)
        {
            for (int i = 1; i <= _maxAttempts; i++)
            {
                var name = string.Format(_pattern, baseId, chainIndex, i);
                if (!isNameTaken(name))
                    return name;
            }
            throw new InvalidOperationException($"Failed to generate unique ID for '{baseId}' after {_maxAttempts} attempts.");
        }

        public void Update(string pattern, int maxAttempts)
        {
            _pattern = pattern;
            _maxAttempts = maxAttempts;
        }
    }

    public interface INameGenerator
    {
        string GenerateName(string? prefix = null);
        string GenerateId(string? category = null);
    }

    public class DefaultNameGenerator : INameGenerator
    {
        private int _counter;
        public string GenerateName(string? prefix = null)
            => $"{prefix ?? "item"}_{Interlocked.Increment(ref _counter)}";
        public string GenerateId(string? category = null)
            => $"{category ?? "id"}_{Guid.NewGuid():N}";
    }

    public interface IChainIdGenerator
    {
        string GenerateId();
    }

    public class DefaultChainIdGenerator : IChainIdGenerator
    {
        public string GenerateId() => $"chain_{Guid.NewGuid():N}";
    }

    public interface IReferenceResolver<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        object Resolve(object value, IReadOnlyGraph<TKey, TNodeData, object> graph);
    }

    public class DefaultReferenceResolver<TKey, TNodeData> : IReferenceResolver<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        public object Resolve(object value, IReadOnlyGraph<TKey, TNodeData, object> graph)
            => value;
    }

    public interface INodeRepository<TNode>
    {
        void AddOrUpdateNode(string key, TNode node);
        TNode? GetNode(string key);
        bool RemoveNode(string key);
        bool ContainsNode(string key);
        IEnumerable<string> GetAllNodeKeys();
        IEnumerable<TNode> GetAllNodes();
        int Count { get; }
    }

    public class InMemoryNodeRepository<TNode> : INodeRepository<TNode>
    {
        private readonly ConcurrentDictionary<string, TNode> _store = new();

        public void AddOrUpdateNode(string key, TNode node) => _store[key] = node;
        public TNode? GetNode(string key) => _store.TryGetValue(key, out var node) ? node : default;
        public bool RemoveNode(string key) => _store.TryRemove(key, out _);
        public bool ContainsNode(string key) => _store.ContainsKey(key);
        public IEnumerable<string> GetAllNodeKeys() => _store.Keys;
        public IEnumerable<TNode> GetAllNodes() => _store.Values;
        public int Count => _store.Count;
    }

    public interface IFactoryManager
    {
        ICommandFactory<TKey, TNodeData, TEdgeData> GetCommandFactory<TKey, TNodeData, TEdgeData>() where TKey : notnull where TNodeData : class;

        IUniqueIdGenerator GetUniqueIdGenerator();

        INameGenerator GetNameGenerator();

        IObjectCloner<T> GetObjectCloner<T>() where T : class;

        IReferenceResolver<TKey, TNodeData> GetReferenceResolver<TKey, TNodeData>() where TKey : notnull where TNodeData : class;

        IChainIdGenerator GetChainIdGenerator();

        INodeRepository<TNode> GetNodeRepository<TNode>();

        ICacheService GetCacheService();

        ILockManager GetLockManager();
    }

    public class DefaultFactoryManager : IFactoryManager
    {
        private readonly ConcurrentDictionary<Type, object> _factories = new();

        public ICommandFactory<TKey, TNodeData, TEdgeData> GetCommandFactory<TKey, TNodeData, TEdgeData>() where TKey : notnull where TNodeData : class
        {
            var factoryType = typeof(DefaultCommandFactory<TKey, TNodeData, TEdgeData>);
            return (ICommandFactory<TKey, TNodeData, TEdgeData>)_factories.GetOrAdd(factoryType, _ => new DefaultCommandFactory<TKey, TNodeData, TEdgeData>());
        }

        public IUniqueIdGenerator GetUniqueIdGenerator()
        {
            return (IUniqueIdGenerator)_factories.GetOrAdd(typeof(IUniqueIdGenerator), _ => new DefaultUniqueIdGenerator());
        }

        public INameGenerator GetNameGenerator()
        {
            return (INameGenerator)_factories.GetOrAdd(typeof(INameGenerator), _ => new DefaultNameGenerator());
        }

        public IObjectCloner<T> GetObjectCloner<T>() where T : class
        {
            var factoryType = typeof(DeepClonerAdapter<T>);
            return (IObjectCloner<T>)_factories.GetOrAdd(factoryType, _ => new DeepClonerAdapter<T>());
        }

        public IReferenceResolver<TKey, TNodeData> GetReferenceResolver<TKey, TNodeData>() where TKey : notnull where TNodeData : class
        {
            var factoryType = typeof(DefaultReferenceResolver<TKey, TNodeData>);
            return (IReferenceResolver<TKey, TNodeData>)_factories.GetOrAdd(factoryType, _ => new DefaultReferenceResolver<TKey, TNodeData>());
        }

        public IChainIdGenerator GetChainIdGenerator()
        {
            return (IChainIdGenerator)_factories.GetOrAdd(typeof(IChainIdGenerator), _ => new DefaultChainIdGenerator());
        }

        public INodeRepository<TNode> GetNodeRepository<TNode>()
        {
            var factoryType = typeof(InMemoryNodeRepository<TNode>);
            return (INodeRepository<TNode>)_factories.GetOrAdd(factoryType, _ => new InMemoryNodeRepository<TNode>());
        }

        public ICacheService GetCacheService()
        {
            return (ICacheService)_factories.GetOrAdd(typeof(ICacheService), _ => new InMemoryCacheService());
        }

        public ILockManager GetLockManager()
        {
            return (ILockManager)_factories.GetOrAdd(typeof(ILockManager), _ => new ReaderWriterLockManager());
        }
    }

    #endregion
}
namespace Locking
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
    }
}

#endregion








namespace Transactions
{
    public interface ITransaction : IDisposable
    {
        void AddCompensation(Action compensation);

        void AddCompensation(Func<Task> asyncCompensation);

        void Commit();
    }

    public sealed class Transaction : ITransaction
    {
        private readonly Stack<Func<Task>> _compensations = new Stack<Func<Task>>();
        private bool _committed;
        private readonly Transaction? _parent;
        private static readonly AsyncLocal<Transaction?> _current = new AsyncLocal<Transaction?>();

        public static Transaction? Current => _current.Value;

        public Transaction()
        {
            _parent = _current.Value;
            _current.Value = this;
        }

        public void AddCompensation(Action compensation)
        {
            if (compensation == null) throw new ArgumentNullException(nameof(compensation));
            AddCompensationCore(() => { compensation(); return Task.CompletedTask; });
        }

        public void AddCompensation(Func<Task> asyncCompensation)
        {
            if (asyncCompensation == null) throw new ArgumentNullException(nameof(asyncCompensation));
            AddCompensationCore(asyncCompensation);
        }

        private void AddCompensationCore(Func<Task> compensation)
        {
            if (_committed) throw new InvalidOperationException("Cannot add compensation after transaction committed.");

            if (_parent != null)
            {
                _parent.AddCompensationCore(compensation);
            }
            else
            {
                _compensations.Push(compensation);
            }
        }

        public void Commit()
        {
            if (_committed)
                throw new InvalidOperationException("Transaction already committed.");

            if (_parent != null)
                throw new InvalidOperationException("Cannot commit an inner transaction. Only the outermost transaction can be committed.");

            _committed = true;
        }

        public void Dispose()
        {
            try
            {
                if (_parent == null && !_committed)
                {
                    Task.Run(RollbackAsync).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Rollback failed during Dispose: {ex}");
            }
            finally
            {
                _current.Value = _parent;
            }
        }

        private async Task RollbackAsync()
        {
            var exceptions = new List<Exception>();
            while (_compensations.Count > 0)
            {
                try
                {
                    var compensation = _compensations.Pop();
                    await compensation().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    Trace.TraceError($"Transaction compensation failed: {ex}");
                }
            }

            if (exceptions.Count > 0)
                throw new AggregateException("One or more compensations failed during rollback.", exceptions);
        }
    }

    public static class TransactionExtensions
    {
        public static void ExecuteInTransaction(Action<ITransaction> action)
        {
            if (Transaction.Current != null)
                throw new InvalidOperationException("ExecuteInTransaction cannot be called inside an existing transaction.");

            using (var tx = new Transaction())
            {
                action(tx);
                tx.Commit();
            }
        }

        public static async Task ExecuteInTransactionAsync(Func<ITransaction, Task> asyncAction)
        {
            if (Transaction.Current != null)
                throw new InvalidOperationException("ExecuteInTransactionAsync cannot be called inside an existing transaction.");

            using (var tx = new Transaction())
            {
                await asyncAction(tx);
                tx.Commit();
            }
        }
    }
}




#endregion






namespace ApplicationWorld.Core
{
    public interface ILogger
    {
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message, Exception ex = null);
    }

    public interface IWorldContext : IDisposable
    {
        T GetData<T>(string key = null);
        void SetData<T>(T value, string key = null);
        bool TryGetData<T>(out T value, string key = null);
        bool ContainsData<T>(string key = null);
        T GetService<T>();
        void RegisterService<T>(T service);
    }

    public interface IWorldSystem
    {
        string Name { get; }
        Task InitializeAsync(IWorldContext context);
        Task UpdateAsync(float deltaTime);
        Task ShutdownAsync();
    }

    public interface IWorldPlugin
    {
        Task OnWorldInitializedAsync(IWorldContext context);
        Task OnWorldUpdatingAsync(float deltaTime);
        Task OnWorldShuttingDownAsync();
    }

    public interface IConfigurable
    {
        void Configure(object config);
    }

    public class DefaultWorldContext : IWorldContext
    {
        private readonly ConcurrentDictionary<string, object> _data = new();
        private readonly ConcurrentDictionary<Type, object> _services = new();
        private bool _disposed;
        private readonly object _disposeLock = new object();

        private static string BuildKey<T>(string key) =>
            string.IsNullOrEmpty(key) ? typeof(T).FullName : $"{typeof(T).FullName}:{key}";

        public T GetData<T>(string key = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            var k = BuildKey<T>(key);
            if (_data.TryGetValue(k, out var value) && value is T typed)
                return typed;
            throw new KeyNotFoundException($"Data of type {typeof(T).Name} with key '{key}' not found.");
        }

        public void SetData<T>(T value, string key = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            _data[BuildKey<T>(key)] = value;
        }

        public bool TryGetData<T>(out T value, string key = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            var k = BuildKey<T>(key);
            if (_data.TryGetValue(k, out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool ContainsData<T>(string key = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            return _data.ContainsKey(BuildKey<T>(key));
        }

        public T GetService<T>()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            if (_services.TryGetValue(typeof(T), out var service) && service is T typed)
                return typed;
            throw new KeyNotFoundException($"Service of type {typeof(T).Name} not registered.");
        }

        public void RegisterService<T>(T service)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultWorldContext));
            _services[typeof(T)] = service;
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_disposeLock)
            {
                if (_disposed) return;
                foreach (var value in _data.Values)
                {
                    if (value is IDisposable disposable)
                        disposable.Dispose();
                }
                _data.Clear();
                _services.Clear();
                _disposed = true;
            }
        }
    }

    public class WorldOptions
    {
        public bool EnableParallelUpdate { get; set; } = false;
        public TimeSpan SystemInitTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan SystemShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public Dictionary<string, object> SystemConfigs { get; } = new();
        public ILogger Logger { get; set; }
        public bool ContinueOnUpdateError { get; set; } = true;
    }

    public class WorldBuilder
    {
        private IWorldContext _context;
        private readonly List<SystemRegistration> _systemRegistrations = new();
        private readonly List<IWorldPlugin> _plugins = new();
        private readonly WorldOptions _options = new();
        private string[] _updateOrder;
        private IServiceProvider _serviceProvider;

        private class SystemRegistration
        {
            public Type SystemType { get; set; }
            public IWorldSystem Instance { get; set; }
            public string Name { get; set; }
            public object Config { get; set; }
        }

        public WorldBuilder UseContext(IWorldContext context) { _context = context; return this; }
        public WorldBuilder UseServiceProvider(IServiceProvider serviceProvider) { _serviceProvider = serviceProvider; return this; }

        public WorldBuilder AddSystem(IWorldSystem system, string name = null, object config = null)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            _systemRegistrations.Add(new SystemRegistration
            {
                Instance = system,
                Name = name ?? system.GetType().FullName,
                Config = config
            });
            return this;
        }

        public WorldBuilder AddSystem<T>(string name = null, object config = null) where T : IWorldSystem
        {
            _systemRegistrations.Add(new SystemRegistration
            {
                SystemType = typeof(T),
                Name = name ?? typeof(T).FullName,
                Config = config
            });
            return this;
        }

        public WorldBuilder AddPlugin(IWorldPlugin plugin)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            _plugins.Add(plugin);
            return this;
        }

        public WorldBuilder SetUpdateOrder(params string[] systemNames)
        {
            _updateOrder = systemNames?.ToArray() ?? Array.Empty<string>();
            return this;
        }

        public WorldBuilder Configure(Action<WorldOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        public async Task<ApplicationWorld> BuildAsync()
        {
            _options.Logger?.LogInformation("Building ApplicationWorld...");
            _context ??= new DefaultWorldContext();

            var systems = new List<IWorldSystem>();
            var nameSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reg in _systemRegistrations)
            {
                IWorldSystem system = reg.Instance;
                if (system == null)
                {
                    system = _serviceProvider != null
                        ? (IWorldSystem)_serviceProvider.GetService(reg.SystemType)
                        : (IWorldSystem)Activator.CreateInstance(reg.SystemType);
                    if (system == null)
                        throw new InvalidOperationException($"Unable to resolve system type {reg.SystemType}.");
                }
                if (!nameSet.Add(system.Name))
                    throw new InvalidOperationException($"Duplicate system name '{system.Name}'.");
                reg.Instance = system;
                systems.Add(system);
            }

            foreach (var reg in _systemRegistrations.Where(r => r.Config != null))
            {
                _context.SetData(reg.Config, $"SystemConfig:{reg.Name}");
                if (reg.Instance is IConfigurable configurable)
                    configurable.Configure(reg.Config);
            }

            using var initCts = new CancellationTokenSource(_options.SystemInitTimeout);
            var initializedSystems = new List<IWorldSystem>();
            try
            {
                foreach (var system in systems)
                {
                    _options.Logger?.LogInformation($"Initializing system '{system.Name}'...");
                    await system.InitializeAsync(_context).ConfigureAwait(false);
                    initializedSystems.Add(system);
                    initCts.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex)
            {
                _options.Logger?.LogError($"Initialization failed, rolling back.", ex);
                try
                {
                    await ShutdownSystemsCoreAsync(initializedSystems, _options.SystemShutdownTimeout, _options.Logger);
                }
                catch (Exception rollbackEx)
                {
                    _options.Logger?.LogError($"Rollback error.", rollbackEx);
                }
                throw new InvalidOperationException("World building failed.", ex);
            }

            var updateList = BuildUpdateList(systems);

            foreach (var plugin in _plugins)
            {
                try { await plugin.OnWorldInitializedAsync(_context).ConfigureAwait(false); }
                catch (Exception ex) { _options.Logger?.LogError($"Plugin OnWorldInitializedAsync failed.", ex); }
            }

            _options.Logger?.LogInformation("ApplicationWorld built successfully.");
            return new ApplicationWorld(_context, updateList, _plugins.AsReadOnly(), _options);
        }

        private List<IWorldSystem> BuildUpdateList(List<IWorldSystem> systems)
        {
            if (_updateOrder == null || _updateOrder.Length == 0)
                return systems.ToList();

            var dict = systems.ToDictionary(s => s.Name);
            var ordered = new List<IWorldSystem>();
            var remaining = new HashSet<IWorldSystem>(systems);

            foreach (var name in _updateOrder)
            {
                if (dict.TryGetValue(name, out var system) && remaining.Contains(system))
                {
                    ordered.Add(system);
                    remaining.Remove(system);
                }
                else
                {
                    _options.Logger?.LogWarning($"Update order issue: '{name}' not found or duplicate.");
                }
            }

            ordered.AddRange(remaining);
            return ordered;
        }

        internal static async Task ShutdownSystemsCoreAsync(IEnumerable<IWorldSystem> systems, TimeSpan timeout, ILogger logger)
        {
            using var cts = new CancellationTokenSource(timeout);
            foreach (var system in systems.Reverse())
            {
                if (cts.IsCancellationRequested)
                {
                    logger?.LogWarning("Shutdown timed out, stopping further shutdowns.");
                    break;
                }
                try
                {
                    await system.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Error shutting down '{system.Name}'.", ex);
                }
            }
        }
    }

    public class ApplicationWorld : IAsyncDisposable
    {
        private readonly IWorldContext _context;
        private readonly IReadOnlyList<IWorldSystem> _updateList;
        private readonly IReadOnlyList<IWorldPlugin> _plugins;
        private readonly WorldOptions _options;
        private readonly CancellationTokenSource _shutdownCts = new();
        private bool _isDisposed;
        private readonly ILogger _logger;
        private const long UpdateTimeWarningThresholdMs = 100;

        public IWorldContext Context => _context;
        public bool IsInitialized { get; private set; } = true;

        internal ApplicationWorld(IWorldContext context, List<IWorldSystem> updateList,
            IReadOnlyList<IWorldPlugin> plugins, WorldOptions options)
        {
            _context = context;
            _updateList = updateList;
            _plugins = plugins;
            _options = options;
            _logger = options.Logger;
        }

        public async Task UpdateAsync(float deltaTime)
        {
            ThrowIfDisposed();
            if (_shutdownCts.IsCancellationRequested) return;

            if (_options.EnableParallelUpdate)
                await ParallelUpdateAsync(deltaTime).ConfigureAwait(false);
            else
                await SequentialUpdateAsync(deltaTime).ConfigureAwait(false);

            foreach (var plugin in _plugins)
            {
                if (_shutdownCts.IsCancellationRequested) break;
                try { await plugin.OnWorldUpdatingAsync(deltaTime).ConfigureAwait(false); }
                catch (Exception ex) { _logger?.LogError($"Plugin OnWorldUpdatingAsync failed.", ex); }
            }
        }

        private async Task SequentialUpdateAsync(float deltaTime)
        {
            foreach (var system in _updateList)
            {
                if (_shutdownCts.IsCancellationRequested) break;
                await ExecuteSystemUpdateAsync(system, deltaTime, isParallel: false).ConfigureAwait(false);
            }
        }

        private async Task ParallelUpdateAsync(float deltaTime)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            var tasks = _updateList.Select(system => Task.Run(async () =>
            {
                if (cts.Token.IsCancellationRequested) return;
                await ExecuteSystemUpdateAsync(system, deltaTime, isParallel: true, cts).ConfigureAwait(false);
            }, cts.Token)).ToList();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError("One or more parallel updates failed.", ex);
                if (!_options.ContinueOnUpdateError)
                    throw;
            }
        }

        private async Task ExecuteSystemUpdateAsync(IWorldSystem system, float deltaTime, bool isParallel, CancellationTokenSource cts = null)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await system.UpdateAsync(deltaTime).ConfigureAwait(false);
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs > UpdateTimeWarningThresholdMs)
                    _logger?.LogWarning($"System '{system.Name}' update took {elapsedMs}ms.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"System '{system.Name}' update failed{(isParallel ? " in parallel mode" : "")}.", ex);
                if (!_options.ContinueOnUpdateError)
                {
                    cts?.Cancel();
                    throw;
                }
            }
        }

        public async Task ShutdownAsync()
        {
            if (_isDisposed) return;
            _logger?.LogInformation("Shutting down ApplicationWorld...");
            _shutdownCts.Cancel();

            await WorldBuilder.ShutdownSystemsCoreAsync(_updateList, _options.SystemShutdownTimeout, _logger);

            foreach (var plugin in _plugins)
            {
                try { await plugin.OnWorldShuttingDownAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger?.LogError($"Plugin OnWorldShuttingDownAsync failed.", ex); }
            }

            _context.Dispose();
            _isDisposed = true;
            IsInitialized = false;
            _logger?.LogInformation("ApplicationWorld shut down.");
        }

        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync().ConfigureAwait(false);
            _shutdownCts.Dispose();
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ApplicationWorld));
        }
    }
}




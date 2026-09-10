
using System.Collections.Concurrent;
using Modules.StateManagement;
using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement
{
    /// <summary>
    /// scope 分区状态管理器。可选注入 <see cref="IStorageEngine"/>（InternalStorage 抽象）：
    /// 注入时 scope 落 <c>StorageEngine.CreateSession</c>、GlobalStore 落根会话（对齐 IStateCenter 设计意图），
    /// 具体存储后端由组合根决定；未注入时回退到纯内存实现，保持向后兼容。
    /// </summary>
    public class StateManager : IStateManager, IDisposable
    {
        private readonly ConcurrentDictionary<string, IStateStore> _scopes = new(StringComparer.OrdinalIgnoreCase);
        private readonly IStateStore _globalStore;
        private readonly IStorageEngine? _storageEngine;
        private bool _disposed;

        public StateManager(IStateStore? globalStore = null, IStorageEngine? storageEngine = null)
        {
            _storageEngine = storageEngine;
            _globalStore = globalStore ?? (storageEngine != null
                ? new StorageCellStateStore(storageEngine.RootCell)
                : new InMemoryStateStore());
        }

        public IStateStore GlobalStore => _globalStore;

        public IStateStore CreateScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                throw new ArgumentException("Scope name cannot be empty", nameof(scopeName));

            IStateStore scope;
            if (_storageEngine != null)
            {
                // 重复 scope → StorageEngine.CreateSession 抛 InvalidOperationException。
                scope = new StorageCellStateStore(_storageEngine.CreateSession(scopeName));
            }
            else
            {
                scope = new InMemoryStateStore();
            }

            if (!_scopes.TryAdd(scopeName, scope))
            {
                // 防御：字典登记失败（重复）时回收已创建的引擎会话，保持原"抛异常"语义。
                if (_storageEngine != null && _storageEngine.GetSession(scopeName) != null)
                {
                    _storageEngine.DisposeSession(scopeName);
                }

                throw new InvalidOperationException($"Scope '{scopeName}' already exists.");
            }

            return scope;
        }

        public IStateStore? GetScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                return null;

            return _scopes.TryGetValue(scopeName, out var scope) ? scope : null;
        }

        public bool ReleaseScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                return false;

            if (!_scopes.TryRemove(scopeName, out _))
                return false;

            if (_storageEngine != null && _storageEngine.GetSession(scopeName) != null)
            {
                _storageEngine.DisposeSession(scopeName);
            }

            return true;
        }

        public IReadOnlyCollection<string> GetScopeNames() => _scopes.Keys.ToArray();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_storageEngine != null)
            {
                foreach (var name in _scopes.Keys.ToList())
                {
                    if (_storageEngine.GetSession(name) != null)
                    {
                        _storageEngine.DisposeSession(name);
                    }
                }
            }

            _scopes.Clear();
        }
    }
}

using System.Collections.Concurrent;
using BasicMethod.Block;
using Modules.InternalStorage.Core;
using Share.StateManagement;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using ShareStateStore = Share.StateManagement.IStateStore;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 状态中心（StateCenter）——全量状态唯一权威（治 L1–L5 架构层收敛）。
    /// 聚合：StateManager 语义（scope 分区）、StateOrchestrator（Actor，保留）、
    /// StateSubscriptionHub（订阅/版本去重）、三个生命周期 Provider（未注册前调用抛异常，装配顺序可见）。
    /// </summary>
    public sealed class StateCenter : IStateCenter
    {
        private readonly IStorageCell _cell;
        private readonly StateSubscriptionHub _hub;
        private readonly StateStore _globalStore;
        private readonly StateOrchestrator _orchestrator;
        private readonly ConcurrentDictionary<string, ShareStateStore> _scopeStores = new(StringComparer.Ordinal);

        private IScopeLifecycle? _scopeLifecycle;
        private ICompositionLifecycle? _compositionLifecycle;
        private IContainerLifecycle? _containerLifecycle;
        private readonly IPublishDispatcher? _dispatcher;
        private bool _disposed;

        public StateCenter(IStorageCell cell, IPublishDispatcher? publishDispatcher = null)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _dispatcher = publishDispatcher;
            // P3/P4 装配口：可注入发布派发器（如 PublishDispatcherAdapter(Modules.Threading.SerialDispatcher)）。
            // null = 内联同步交付订阅 handler / StateChanged（现状零回归）；非 null = 广播与 StateChanged 均经派发器
            // （后台串行/按订阅上下文回投），写线程只提交不被订阅回调阻塞（两级同步广播统一解耦）。
            _hub = new StateSubscriptionHub().ConfigureDispatch(publishDispatcher);
            _globalStore = new StateStore(cell, publishDispatcher);
            _orchestrator = new StateOrchestrator(_globalStore);
        }

        // ── IStateCenter ──

        public IStorageCell Cell => _cell;

        public ShareStateStore GlobalStore => _globalStore;

        public ShareStateStore GetOrCreateScope(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ThrowIfDisposed();

            // scope 状态分区：根会话下的子 cell（parent 链回退，等价 StorageEngine.CreateSession 分区语义）
            // GetOrAdd 原子创建，消除原索引赋值（_scopeStores[scope] = store）的并发覆盖
            return _scopeStores.GetOrAdd(scope, s =>
            {
                var scopeCell = new StorageCell($"StateCenter.Scope.{s}", _cell);
                return new StateStore(scopeCell, _dispatcher); // P4：scope store 的 StateChanged 与根 store 共享同一派发器
            });
        }

        public IStateTransaction BeginTransaction(string? scope = null)
        {
            ThrowIfDisposed();

            // scope 事务定向到 scope 分区 cell（与 GetOrCreateScope 隔离语义一致）；
            // 无 scope 落根 cell。原实现恒写根 cell，scope 参数仅作装饰（未真正隔离）。
            // scope 分区 store 均为 StateStore 实例（GetOrCreateScope 创建），转具体类型取 Cell 安全。
            var cell = string.IsNullOrWhiteSpace(scope)
                ? _cell
                : ((StateStore)GetOrCreateScope(scope)).Cell;

            return new StateTransaction(cell, _hub, scope);
        }

        public IStateSubscriptionHub Subscriptions => _hub;

        public IScopeLifecycle Scopes
            => _scopeLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：IScopeLifecycle（StateCenter 装配顺序错误，请在 StateCenterFactory 注册）");

        public ICompositionLifecycle Composition
            => _compositionLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：ICompositionLifecycle（StateCenter 装配顺序错误，请在 StateCenterFactory 注册）");

        public IContainerLifecycle Containers
            => _containerLifecycle ?? throw new InvalidOperationException("生命周期 Provider 未注册：IContainerLifecycle（StateCenter 装配顺序错误，请在 StateCenterFactory 注册）");

        public void RegisterScopeLifecycle(IScopeLifecycle scopeLifecycle)
        {
            _scopeLifecycle = scopeLifecycle ?? throw new ArgumentNullException(nameof(scopeLifecycle));
        }

        public void RegisterCompositionLifecycle(ICompositionLifecycle compositionLifecycle)
        {
            _compositionLifecycle = compositionLifecycle ?? throw new ArgumentNullException(nameof(compositionLifecycle));
        }

        public void RegisterContainerLifecycle(IContainerLifecycle containerLifecycle)
        {
            _containerLifecycle = containerLifecycle ?? throw new ArgumentNullException(nameof(containerLifecycle));
        }

        public TState? Read<TState>(StateKey key)
        {
            ThrowIfDisposed();
            return _cell.Get<TState>(key.FullKey, StorageLayer.Session);
        }

        /// <summary>按 StateKey 读状态（指定存储层）。</summary>
        public TState? Read<TState>(StateKey key, StorageLayer layer)
        {
            ThrowIfDisposed();
            return _cell.Get<TState>(key.FullKey, layer);
        }

        public void Write<TState>(StateKey key, TState value, string? source = null)
        {
            Write(key, value, StorageLayer.Session, source);
        }

        /// <summary>非事务直接写（指定存储层）。</summary>
        public void Write<TState>(StateKey key, TState value, StorageLayer layer, string? source = null)
        {
            ThrowIfDisposed();

            // 广播分级（Transient 隔离）：Transient 层为「轮内执行期瞬态」（Agent Vars / 骨架临时空间），
            // 属写线程本地、数量多、无需逐 key 投影。写侧只做静默持久化（SetQuiet：不 raise DataChanged、不进 StateChanged
            // 转发），且不进全局 hub（不逐 key Publish）；投影方在轮边界读取汇总快照而非逐 key 广播，
            // 从根源消除「写线程被瞬态订阅/广播焊死」——写线程只提交、不被两级同步广播阻塞。
            // Session/Config（持久/跨轮）保持原两级广播路径（可经装配 IPublishDispatcher 出队解耦）。
            if (layer == StorageLayer.Transient)
            {
                _cell.SetQuiet(key.FullKey, value, layer);
                return;
            }

            _cell.Set(key.FullKey, value, layer);
            var version = _hub.GetLastVersion(key.Namespace, key.Scope) + 1;
            _hub.Publish(key, value, version, source);
        }

        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;

            // 进程退出唯一释放点：内部调用 Composition.AppShutdown()（R3）
            _compositionLifecycle?.AppShutdown();

            // Actor 资源释放（原实现遗漏：Shutdown 未关闭 Actor）
            try
            {
                _orchestrator.ShutdownAllActorsAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Shutdown 承诺不抛：Actor 关闭失败仅跳过（资源由进程退出回收）
            }

            _hub.Dispose();
            _scopeStores.Clear();
        }

        // ── IStateManager（scope 分区） ──

        ShareStateStore IStateManager.CreateScope(string scopeName) => GetOrCreateScope(scopeName);

        ShareStateStore? IStateManager.GetScope(string scopeName)
            => _scopeStores.TryGetValue(scopeName, out var store) ? store : null;

        bool IStateManager.ReleaseScope(string scopeName)
            => _scopeStores.TryRemove(scopeName, out _);

        IReadOnlyCollection<string> IStateManager.GetScopeNames()
            => _scopeStores.Keys.ToArray();

        // ── IStateOrchestrator（Actor，保留） ──

        public ShareStateStore Store => _orchestrator.Store;

        public void RegisterActor(IStateActor actor) => _orchestrator.RegisterActor(actor);

        public IStateActor? GetActor(string name) => _orchestrator.GetActor(name);

        public Task<PipelineResult> ExecutePipelineAsync(ExecutionPipeline pipeline)
            => _orchestrator.ExecutePipelineAsync(pipeline);

        public Task InitializeAllActorsAsync() => _orchestrator.InitializeAllActorsAsync();

        public Task ShutdownAllActorsAsync() => _orchestrator.ShutdownAllActorsAsync();

        public void Reset() => _orchestrator.Reset();

        // ── IDisposable ──

        public void Dispose() => Shutdown();

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StateCenter));
        }
    }
}

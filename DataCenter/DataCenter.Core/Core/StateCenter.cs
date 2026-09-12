using System.Collections.Concurrent;
using BasicMethod.Block;
using DataCenter.Abstractions.Cells;
using Operation.StateBlock.Core.Compat;
using Share.StateManagement;
using TConfig;
using TConfig.ContextFrame.Abstractions;
using ShareStateStore = Share.StateManagement.IStateStore;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 数据单元宿主（端口侧接缝）—— 使分层键客户端在端口真源上工作，而不经引擎存储契约。
    /// </summary>
    public interface IDataCellProvider
    {
        /// <summary>根数据单元（端口真源）。</summary>
        IDataCell Root { get; }
    }

    /// <summary>
    /// 状态中心（StateCenter）——全量状态唯一权威。
    /// 存储后端经端口 <see cref="IDataCell"/> 接入（主路径）；引擎存储（IStorageCell）经 Compat 适配器接入（G2 迁移期兼容）。
    /// 聚合：StateManager 语义（scope 分区）、StateOrchestrator（Actor）、StateSubscriptionHub、
    /// 三个生命周期 Provider（未注册前调用抛异常，装配顺序可见）。
    /// </summary>
    public sealed class StateCenter : IStateCenter, IDataCellProvider
    {
        private readonly IDataCell _root;
        private readonly IDataCellFactory _factory;
        private readonly IStorageCell? _legacyCell;
        private IStorageCell? _rootView;
        private readonly StateSubscriptionHub _hub;
        private readonly StateStore _globalStore;
        private readonly StateOrchestrator _orchestrator;
        private readonly ConcurrentDictionary<string, ShareStateStore> _scopeStores = new(StringComparer.Ordinal);

        private IScopeLifecycle? _scopeLifecycle;
        private ICompositionLifecycle? _compositionLifecycle;
        private IContainerLifecycle? _containerLifecycle;
        private readonly IPublishDispatcher? _dispatcher;
        private bool _disposed;

        /// <summary>
        /// 端口后端构造（G2 主路径）—— 签名内不含任何引擎/宿主设施类型。
        /// </summary>
        /// <param name="root">根数据单元（端口实现，如 InMemoryDataCell）。</param>
        /// <param name="factory">scope 分区子单元工厂；null = 内存工厂。</param>
        /// <param name="publishDispatcher">可选派发器：null = 内联同步交付订阅回调；非 null = 出队解耦写线程。</param>
        public StateCenter(IDataCell root, IDataCellFactory? factory = null, IPublishDispatcher? publishDispatcher = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _factory = factory ?? new InMemoryDataCellFactory();
            _dispatcher = publishDispatcher;
            _hub = new StateSubscriptionHub().ConfigureDispatch(publishDispatcher);
            _globalStore = new StateStore(_root, publishDispatcher);
            _orchestrator = new StateOrchestrator(_globalStore);
        }

        /// <summary>
        /// 引擎后端构造（迁移期兼容：IStorageCell 经 <see cref="StorageCellAdapter"/> 接入端口，存量消费方零改动）。
        /// </summary>
        public StateCenter(IStorageCell cell, IPublishDispatcher? publishDispatcher = null)
            : this(cell, null, publishDispatcher)
        {
        }

        /// <summary>
        /// 引擎后端构造（指定 scope 分区载体工厂；F-35 方案 A）。
        /// <para>
        /// 分区载体<b>按后端选择</b>：legacy 后端须经引擎 <c>CreateChildCell</c> 派生会话子单元并把该子单元适配为
        /// <see cref="IDataCell"/>（否则 scope 状态落入与引擎无关的内存子单元：无引擎快照语义）。工厂由持有引擎实现的上层
        /// （<c>DataCenter.Adapters.StorageCellPartitionFactory</c>）提供；null = 端口内存工厂（迁移期默认，行为不变）。
        /// </para>
        /// </summary>
        public StateCenter(IStorageCell cell, IDataCellFactory? partitionCellFactory, IPublishDispatcher? publishDispatcher = null)
            : this(new StorageCellAdapter(cell ?? throw new ArgumentNullException(nameof(cell))), partitionCellFactory, publishDispatcher)
        {
            _legacyCell = cell;
        }

        /// <summary>根数据单元（分层键客户端经此访问端口真源）。</summary>
        public IDataCell Root => _root;

        // ── IStateCenter ──

        /// <summary>统一存储后端：兼容后端直返原实例（零语义漂移）；端口后端返回契约视图。</summary>
        public IStorageCell Cell => _legacyCell ?? (_rootView ??= new DataCellStorageView(_root));

        public ShareStateStore GlobalStore => _globalStore;

        public ShareStateStore GetOrCreateScope(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ThrowIfDisposed();

            // scope 状态分区：根单元下派生子单元（父链读回退，等价原 StorageCell 子会话语义）；
            // GetOrAdd 原子创建，消除原索引赋值的并发覆盖。
            return _scopeStores.GetOrAdd(scope, s =>
            {
                var scopeCell = _factory.CreateChild($"StateCenter.Scope.{s}", _root);
                return new StateStore(scopeCell, _dispatcher);
            });
        }

        public IStateTransaction BeginTransaction(string? scope = null)
        {
            ThrowIfDisposed();

            // scope 事务定向到 scope 分区存储（与 GetOrCreateScope 隔离语义一致）；无 scope 落根存储。
            var cell = string.IsNullOrWhiteSpace(scope)
                ? Cell
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
            return _root.Get<TState>(key.FullKey, DataTier.Session);
        }

        /// <summary>按 StateKey 读状态（指定存储层）。</summary>
        public TState? Read<TState>(StateKey key, StorageLayer layer)
        {
            ThrowIfDisposed();
            return _root.Get<TState>(key.FullKey, layer.ToTier());
        }

        public void Write<TState>(StateKey key, TState value, string? source = null)
        {
            Write(key, value, StorageLayer.Session, source);
        }

        /// <summary>非事务直接写（指定存储层）。</summary>
        public void Write<TState>(StateKey key, TState value, StorageLayer layer, string? source = null)
        {
            ThrowIfDisposed();

            // 广播分级（Transient 隔离）：Transient 层为作用域内瞬态（写线程本地、数量多、无需逐 key 投影）。
            // 写侧只做静默持久化（SetQuiet：不触发变更通知），且不进全局 hub（不逐 key Publish）；
            // 投影方在边界读取汇总快照而非逐 key 广播，从根源消除「写线程被瞬态订阅/广播焊死」。
            // Session/Config（持久/跨轮）保持原两级广播路径（可经装配 IPublishDispatcher 出队解耦）。
            if (layer == StorageLayer.Transient)
            {
                _root.SetQuiet(key.FullKey, value, DataTier.Transient);
                return;
            }

            _root.Set(key.FullKey, value, layer.ToTier());
            var version = _hub.GetLastVersion(key.Namespace, key.Scope) + 1;
            _hub.Publish(key, value, version, source);
        }

        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;

            // 进程退出唯一释放点：内部调用 Composition.AppShutdown()
            _compositionLifecycle?.AppShutdown();

            // Actor 资源释放（Shutdown 未关闭 Actor 属原实现遗漏）
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

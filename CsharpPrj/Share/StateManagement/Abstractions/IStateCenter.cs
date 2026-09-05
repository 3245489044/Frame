using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement
{
    /// <summary>
    /// 状态中心门面（StateCenter）——全量状态唯一权威。
    /// 聚合 StateManager（scope 分区）、StateOrchestrator（Actor，保留）、
    /// StateSubscriptionHub 与三个生命周期 Provider。
    /// </summary>
    public interface IStateCenter : IStateManager, IStateOrchestrator, IDisposable
    {
        /// <summary>统一存储后端（StorageEngine 根会话）。</summary>
        IStorageCell Cell { get; }

        /// <summary>全局字符串 KV（对齐 IStateManager.GlobalStore）。</summary>
        new IStateStore GlobalStore { get; }

        /// <summary>scope 状态分区（落 StorageEngine.CreateSession）。</summary>
        IStateStore GetOrCreateScope(string scope);

        IStateTransaction BeginTransaction(string? scope = null);

        IStateSubscriptionHub Subscriptions { get; }

        IScopeLifecycle Scopes { get; }

        ICompositionLifecycle Composition { get; }

        IContainerLifecycle Containers { get; }

        /// <summary>生命周期 Provider 注册（FrontEndDriven.State 在装配时注入）。</summary>
        void RegisterScopeLifecycle(IScopeLifecycle scopeLifecycle);

        void RegisterCompositionLifecycle(ICompositionLifecycle compositionLifecycle);

        void RegisterContainerLifecycle(IContainerLifecycle containerLifecycle);

        /// <summary>按 StateKey 读状态（默认 Session 层）。</summary>
        TState? Read<TState>(StateKey key);

        /// <summary>按 StateKey 读状态（指定存储层：Config/Session/Transient）。</summary>
        TState? Read<TState>(StateKey key, StorageLayer layer);

        /// <summary>非事务直接写（诊断/审计用，业务变更必须走事务）。默认 Session 层。</summary>
        void Write<TState>(StateKey key, TState value, string? source = null);

        /// <summary>非事务直接写（指定存储层）。默认 Session 层。</summary>
        void Write<TState>(StateKey key, TState value, StorageLayer layer, string? source = null);

        /// <summary>进程退出清理（内部调用 Composition.AppShutdown() 后释放存储）。</summary>
        void Shutdown();
    }
}

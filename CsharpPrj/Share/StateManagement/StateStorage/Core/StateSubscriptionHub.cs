using System.Collections.Concurrent;

namespace Share.StateManagement
{
    /// <summary>
    /// 订阅中枢：每 (namespace, scope) 维护版本表，Publish 陈旧版本提升为 last+1（不丢弃，防并发丢写）；
    /// handler 异常隔离（单 handler 失败不影响其余）。投影器唯一数据源。
    /// </summary>
    public sealed class StateSubscriptionHub : IStateSubscriptionHub
    {
        private readonly ConcurrentDictionary<string, List<SubscriptionEntry>> _subscriptions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(string Namespace, string Scope), long> _lastSeen = new();
        private readonly object _versionLock = new();
        private bool _disposed;
        // 总线-1：发布派发器（可空）。null = 内联同步执行订阅 handler（现状零回归）；
        // 非 null = 交付订阅回调经派发器（后台串行/按订阅上下文回投），使写线程只提交发布、不被订阅回调阻塞/封送（D3 线程解耦）。
        private volatile IPublishDispatcher? _dispatcher;

        /// <summary>配置发布派发器（总线-1 解耦点）。默认不配置（内联，零回归）；幂等——重复配置以首次非 null 为准。
        /// 建议在首次 Publish（投影启动）前由组合根调用；异步派发需保证同一发布内 delivery 相对顺序并按订阅者线程模型回投。</summary>
        public StateSubscriptionHub ConfigureDispatch(IPublishDispatcher? dispatcher)
        {
            if (dispatcher is not null && _dispatcher is null)
                _dispatcher = dispatcher;
            return this;
        }


        private sealed record SubscriptionEntry(
            string StateNamespace,
            string? Scope,
            Action<StateKey, StateSnapshot<object?>> Handler);

        public IDisposable Subscribe(
            string stateNamespace,
            string? scope,
            Action<StateKey, StateSnapshot<object?>> handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stateNamespace);
            ArgumentNullException.ThrowIfNull(handler);
            ThrowIfDisposed();

            var entry = new SubscriptionEntry(stateNamespace, scope, handler);
            var list = _subscriptions.GetOrAdd(stateNamespace, _ => new List<SubscriptionEntry>());
            lock (list)
            {
                list.Add(entry);
            }

            return new Subscription(this, stateNamespace, entry);
        }

        /// <summary>
        /// 自动分配版本发布：版本在锁内原子分配并落表，保证同一 (namespace, scope) 下单调递增且不丢发布
        /// （修复并发事务各自读 lastSeen+1 导致的版本冲突被去重丢弃的竞态，E1）。
        /// </summary>
        public void Publish(StateKey key, object? value, string? source = null)
        {
            var nsScope = (key.Namespace, key.Scope);

            long version;
            lock (_versionLock)
            {
                version = (_lastSeen.TryGetValue(nsScope, out var l) ? l : 0L) + 1;
                _lastSeen[nsScope] = version;
            }

            Dispatch(key, value, version, source);
        }

        public void Publish(StateKey key, object? value, long version, string? source = null)
        {
            var nsScope = (key.Namespace, key.Scope);

            // 显式版本：仍须在锁内保证单调。冲突（并发重排/重复）时提升为 last+1 而非丢弃，
            // 避免合法发布因版本号被误判为陈旧而静默丢失。
            lock (_versionLock)
            {
                var last = _lastSeen.TryGetValue(nsScope, out var l) ? l : 0L;
                if (version <= last)
                    version = last + 1;
                _lastSeen[nsScope] = version;
            }

            Dispatch(key, value, version, source);
        }

        private void Dispatch(StateKey key, object? value, long version, string? source)
        {
            if (!_subscriptions.TryGetValue(key.Namespace, out var list))
                return;

            List<SubscriptionEntry> snapshot;
            lock (list)
            {
                snapshot = list.ToList();
            }

            if (snapshot.Count == 0) return;

            var snapshotObj = new StateSnapshot<object?>(value, version, DateTime.UtcNow, source);
            var dispatcher = _dispatcher;
            foreach (var entry in snapshot)
            {
                if (entry.Scope != null &&
                    !string.Equals(entry.Scope, key.Scope, StringComparison.Ordinal))
                {
                    continue;
                }

                // 总线-1：未配置派发器 → 内联同步交付（零回归）；配置后 → 经派发器交付（写线程不被订阅回调阻塞/封送）。
                if (dispatcher is null)
                    InvokeSafe(entry.Handler, key, snapshotObj);
                else
                    dispatcher.Dispatch(() => InvokeSafe(entry.Handler, key, snapshotObj));
            }
        }

        /// <summary>handler 调用包装：异常隔离（单 handler 失败不影响其余）——无论派发器内联还是异步，隔离都在此内联保证。</summary>
        private static void InvokeSafe(Action<StateKey, StateSnapshot<object?>> handler, StateKey key, StateSnapshot<object?> snapshot)
        {
            try
            {
                handler(key, snapshot);
            }
            catch
            {
                // handler 异常隔离：单 handler 失败不影响其余
            }
        }

        public long GetLastVersion(string stateNamespace, string scope)
            => _lastSeen.TryGetValue((stateNamespace, scope), out var v) ? v : 0L;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscriptions.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StateSubscriptionHub));
        }

        private sealed class Subscription : IDisposable
        {
            private readonly StateSubscriptionHub _hub;
            private readonly string _namespace;
            private readonly SubscriptionEntry _entry;
            private bool _disposed;

            public Subscription(StateSubscriptionHub hub, string stateNamespace, SubscriptionEntry entry)
            {
                _hub = hub;
                _namespace = stateNamespace;
                _entry = entry;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_hub._subscriptions.TryGetValue(_namespace, out var list))
                {
                    lock (list)
                    {
                        list.Remove(_entry);
                    }
                }
            }
        }
    }
}

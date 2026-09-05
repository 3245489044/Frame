
#nullable enable

using ECS.Core;
using ECS.Abstractions;
using Modules.ComponentData.Abstractions;
using Modules.ComponentData.Extensions;

namespace Modules.ComponentData.Reactor
{
    public class EcsComponentReactorPipeline : ReactorSystemBase
    {
        private IComponentDataStore? _store;
        private IDisposable? _storeSubscription;
        private volatile bool _disposed;

        private sealed class EcsReactorWrapper<T> : IReactorWrapper
        {
            private readonly IEcsComponentReactor<T> _reactor;

            public EcsReactorWrapper(IEcsComponentReactor<T> reactor)
            {
                _reactor = reactor ?? throw new ArgumentNullException(nameof(reactor));
            }

            public float Priority => _reactor.Priority;

            public IEcsComponentReactor<T> InnerReactor => _reactor;

            public bool ShouldReact(object data)
            {
                return data is T typed && _reactor.ShouldReact(typed);
            }

            public void React(object data, object? context)
            {
                if (data is T typed && context is EntityManager em)
                {
                    _reactor.React(typed, em);
                }
            }
        }

        public void Register<T>(IEcsComponentReactor<T> reactor)
        {
            var wrapper = new EcsReactorWrapper<T>(reactor);
            RegisterCore(wrapper, typeof(T));
        }

        public bool Unregister<T>(IEcsComponentReactor<T> reactor)
        {
            var wrapper = new EcsReactorWrapper<T>(reactor);
            return UnregisterCore(wrapper, typeof(T));
        }

        public void Execute(object data, EntityManager entityManager)
        {
            ExecuteCore(data, entityManager);
        }

        public void Initialize(EntityManager em)
        {
            if (em == null)
                throw new ArgumentNullException(nameof(em));

            _store = em.GetComponentDataStore();

            if (_store != null)
            {
                _storeSubscription = ConnectTo(_store, em);
            }
        }

        public IDisposable ConnectTo(IComponentDataStore store, EntityManager entityManager)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (entityManager == null) throw new ArgumentNullException(nameof(entityManager));

            return store.DataChanged.Subscribe(args =>
            {
                if (args.NewValue != null)
                {
                    Execute(args.NewValue, entityManager);
                }
            });
        }

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _storeSubscription?.Dispose();
            _store = null;
            base.Dispose();
        }
    }
}

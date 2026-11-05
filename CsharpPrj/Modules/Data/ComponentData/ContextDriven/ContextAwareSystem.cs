
#nullable enable

using ECS.Core;
using ECS.Abstractions;
using Modules.ComponentData.Extensions;
using Modules.ComponentData.Reactor;
using TConfigContextFrame = TConfig.ContextFrame.Abstractions;

namespace Modules.ComponentData.ContextDriven
{
    public abstract class ContextAwareSystem : SystemBase
    {
        private readonly EcsComponentReactorPipeline _pipeline = new();
        private EcsSkeletonContext? _skeletonContext;

        protected EcsSkeletonContext Context =>
            _skeletonContext ?? throw new InvalidOperationException("Context not initialized");

        protected void RegisterReactor<T>(IEcsComponentReactor<T> reactor)
        {
            _pipeline.Register(reactor);
        }

        public override void Initialize(EntityManager em)
        {
            base.Initialize(em);
            _skeletonContext = em.GetOrCreateSkeletonContext();
            _skeletonContext.DataChanged += OnContextDataChanged;
            _pipeline.Initialize(em);
        }

        private void OnContextDataChanged(object? sender, TConfigContextFrame.ContextDataChangedEventArgs e)
        {
            if (e.NewValue == null) return;
            if (EntityManager != null)
                _pipeline.Execute(e.NewValue, EntityManager);
        }

        public override void Update(float deltaTime) { }

        protected override void OnDispose()
        {
            if (_skeletonContext != null)
                _skeletonContext.DataChanged -= OnContextDataChanged;
            _pipeline.Dispose();
            base.OnDispose();
        }
    }
}

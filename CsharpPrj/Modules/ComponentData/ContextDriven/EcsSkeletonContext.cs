using BasicMethod.ContextFrame.Core;
using ECS.Core;
using Modules.ComponentData.Skeleton;

namespace Modules.ComponentData.ContextDriven
{
    public class EcsSkeletonContext : SkeletonContext
    {
        private readonly IContext _innerContext;
        private readonly WeakReference<EntityManager>? _entityManagerRef;

        public event EventHandler<ContextDataChangedEventArgs>? DataChanged;

        public EntityManager? EntityManager => _entityManagerRef != null && _entityManagerRef.TryGetTarget(out var em) ? em : null;

        public EcsSkeletonContext(IContext? context = null, EntityManager? entityManager = null) : base(context ?? new ContextBase())
        {
            _innerContext = base.Context;
            _innerContext.DataChanged += OnContextDataChanged;

            if (entityManager != null)
                _entityManagerRef = new WeakReference<EntityManager>(entityManager);
        }

        private void OnContextDataChanged(object sender, ContextDataChangedEventArgs e) =>
            DataChanged?.Invoke(this, e);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _innerContext.DataChanged -= OnContextDataChanged;
            base.Dispose(disposing);
        }
    }
}

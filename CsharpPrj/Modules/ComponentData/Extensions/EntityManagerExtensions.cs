
#nullable enable

using ECS.Core;
using BasicMethod.ContextFrame.Core;
using Modules.ComponentData.ContextDriven;
using Modules.ComponentData.Core;
using Modules.ComponentData.Reactor;
using System.Runtime.CompilerServices;

namespace Modules.ComponentData.Extensions
{
    public static class EntityManagerExtensions
    {
        #region SkeletonContext 扩展

        private class ContextHolder
        {
            public EcsSkeletonContext? Context;
        }

        private static readonly ConditionalWeakTable<EntityManager, ContextHolder> _contexts = new();

        public static EcsSkeletonContext GetOrCreateSkeletonContext(this EntityManager em)
        {
            var holder = _contexts.GetValue(em, e => new ContextHolder());
            if (holder.Context == null)
            {
                lock (holder)
                {
                    if (holder.Context == null)
                    {
                        holder.Context = new EcsSkeletonContext(new ContextBase($"ECS-{Guid.NewGuid()}"), em);
                    }
                }
            }
            return holder.Context!;
        }

        public static void SetSkeletonContext(this EntityManager em, EcsSkeletonContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var holder = _contexts.GetValue(em, e => new ContextHolder());
            holder.Context = ctx;
        }

        public static bool TryGetSkeletonContext(this EntityManager em, out EcsSkeletonContext? ctx)
        {
            if (_contexts.TryGetValue(em, out var holder))
            {
                ctx = holder.Context;
                return ctx != null;
            }
            ctx = null;
            return false;
        }

        public static IContext GetOrCreateContext(this EntityManager em) =>
            em.GetOrCreateSkeletonContext().Context;

        public static void SetContext(this EntityManager em, IContext ctx) =>
            em.SetSkeletonContext(new EcsSkeletonContext(ctx));

        public static bool TryGetContext(this EntityManager em, out IContext? ctx)
        {
            if (em.TryGetSkeletonContext(out var skCtx))
            {
                ctx = skCtx?.Context;
                return ctx != null;
            }
            ctx = null;
            return false;
        }

        #endregion

        #region ComponentDataStore 扩展

        public static IComponentDataStore GetComponentDataStore(this EntityManager em)
        {
            if (em == null)
                throw new ArgumentNullException(nameof(em));

            var skCtx = em.GetOrCreateSkeletonContext();

            if (skCtx.TryGetData<IComponentDataStore>(out var existing))
            {
                return existing!;
            }

            var store = new ComponentDataStore();
            skCtx.SetData(store);
            return store;
        }

        public static EcsComponentReactorPipeline GetOrCreateReactorPipeline(this EntityManager em)
        {
            if (em == null)
                throw new ArgumentNullException(nameof(em));

            var skCtx = em.GetOrCreateSkeletonContext();

            if (skCtx.TryGetData<EcsComponentReactorPipeline>(out var existing))
            {
                return existing!;
            }

            var newPipeline = new EcsComponentReactorPipeline();
            newPipeline.Initialize(em);
            skCtx.SetData(newPipeline);
            return newPipeline;
        }

        public static void RegisterReactor<T>(this EntityManager em, IEcsComponentReactor<T> reactor)
        {
            var pipeline = em.GetOrCreateReactorPipeline();
            pipeline.Register(reactor);
        }

        public static void SetComponentData<T>(this EntityManager em, string key, T value)
        {
            var store = em.GetComponentDataStore();
            store.Set(key, value);
        }

        public static T? GetComponentData<T>(this EntityManager em, string key)
        {
            var store = em.GetComponentDataStore();
            return store.Get<T>(key);
        }

        #endregion
    }
}

using ECS.Core;
using FrameworkAbstractions.ECS;
using Modules.ComponentData.Reflection;
using System.Collections.Concurrent;

namespace Modules.ComponentData.Enhanced
{
    internal class EntityManagerEventSource : IComponentEventSource
    {
        private readonly EntityManager _em;
        private static readonly ConcurrentDictionary<Type, Func<EntityManager, Entity, object>> _getComponentDelegates = new();

        public event Action<Entity, Type> ComponentAdded;
        public event Action<Entity, Type> ComponentRemoved;
        public event Action<Entity, Type, object?, object> ComponentUpdated;

        public EntityManagerEventSource(EntityManager em)
        {
            _em = em;
            _em.ComponentAdded += (e, t) => ComponentAdded?.Invoke(e, t);
            _em.ComponentRemoved += (e, t) => ComponentRemoved?.Invoke(e, t);
            _em.ComponentUpdated += (e, t, old, nw) => ComponentUpdated?.Invoke(e, t, old, nw);
        }

        public T GetComponent<T>(Entity entity) where T : struct
        {
            var func = _getComponentDelegates.GetOrAdd(typeof(T), t =>
            {
                var desc = new InvocationDescriptor
                {
                    TargetType = typeof(EntityManager),
                    MemberName = "GetComponent",
                    MemberKind = MemberKind.Method,
                    ParameterTypes = new[] { typeof(Entity) },
                    GenericArguments = new[] { t },
                    IsStatic = false,
                    IsSetter = false
                };
                var delegateType = typeof(Func<EntityManager, Entity, object>);
                var d = DelegateCompiler.GetOrCompile(desc, delegateType);
                return (Func<EntityManager, Entity, object>)d;
            });

            return (T)func(_em, entity);
        }

        public bool IsEntityValid(Entity entity) => _em.IsEntityValid(entity);
    }
}

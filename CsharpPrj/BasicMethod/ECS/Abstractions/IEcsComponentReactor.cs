
#nullable enable

using ECS.Core;

namespace ECS.Abstractions
{
    public interface IEcsComponentReactor<in T>
    {
        float Priority { get; }

        bool ShouldReact(T data);

        void React(T data, EntityManager entityManager);
    }
}

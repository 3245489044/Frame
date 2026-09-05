
using TConfig.ECS;

namespace FrameworkAbstractions.ECS
{
    public interface IComponentLifecycleHandler<T> where T : struct
    {
        void OnAdded(Entity entity, T component) { }
        void OnRemoved(Entity entity, T component) { }
        void OnChanged(Entity entity, T newComponent, T oldComponent) { }
    }
}

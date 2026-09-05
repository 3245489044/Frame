
using TConfig.ECS;

namespace FrameworkAbstractions.ECS
{
    public interface IComponentEventSource
    {
        event Action<Entity, Type> ComponentAdded;
        event Action<Entity, Type> ComponentRemoved;
        event Action<Entity, Type, object?, object> ComponentUpdated;
        T GetComponent<T>(Entity entity) where T : struct;
        bool IsEntityValid(Entity entity);
    }

    public interface IGenericComponentEventSource : IComponentEventSource
    {
        void SubscribeAdded<T>(Action<Entity, T> handler) where T : struct;
        void UnsubscribeAdded<T>(Action<Entity, T> handler) where T : struct;
        void SubscribeRemoved<T>(Action<Entity, T> handler) where T : struct;
        void UnsubscribeRemoved<T>(Action<Entity, T> handler) where T : struct;
        void SubscribeUpdated<T>(Action<Entity, T, T> handler) where T : struct;
        void UnsubscribeUpdated<T>(Action<Entity, T, T> handler) where T : struct;
    }
}

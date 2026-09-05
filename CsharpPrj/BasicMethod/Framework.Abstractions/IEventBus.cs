
namespace Modules.Framework.Abstractions
{
    public interface IEventBus
    {
        void Publish<TEvent>(TEvent @event) where TEvent : class;

        void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class;

        void Unsubscribe<TEvent>(object subscriber) where TEvent : class;

        void Publish(string eventName, object? args);

        void Subscribe(string eventName, Action<object?> handler);

        void Unsubscribe(string eventName, Action<object?> handler);
    }

    public interface IEventRouter
    {
        void RegisterRoute(string eventName, Func<object?, object?, bool> routeAction, int priority = 0);

        void UnregisterRoute(string eventName, Func<object?, object?, bool> routeAction);

        bool Route(string eventName, object? sender, object? args);
    }
}

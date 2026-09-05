
namespace TConfig;

public interface IEventSubscriptionManager : IDisposable
{
    void Subscribe<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        EventHandler<TEventArgs> handler) where TEventArgs : EventArgs;

    void Subscribe<T>(
        Action<Action<T>> subscribe,
        Action<Action<T>> unsubscribe,
        Action<T> handler);

    void Subscribe(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        EventHandler handler);

    int SubscriptionCount { get; }

    void UnsubscribeAll();
}

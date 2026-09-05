
namespace Modules.StateManagement
{
    public static class HandlerRegistryExtensions
    {
        public static void ForEach<TKey, THandler>(
            this IHandlerRegistry<TKey, THandler> registry,
            Action<THandler> action)
            where TKey : notnull
            where THandler : class
        {
            ArgumentNullException.ThrowIfNull(action);

            foreach (var handler in registry.GetAll())
            {
                action(handler.Value);
            }
        }

        public static void RegisterAll<TKey, THandler>(
            this IHandlerRegistry<TKey, THandler> registry,
            params (TKey key, THandler handler)[] handlers)
            where TKey : notnull
            where THandler : class
        {
            foreach (var (key, handler) in handlers)
            {
                registry.Register(key, handler);
            }
        }

        public static bool Contains<TKey, THandler>(
            this IHandlerRegistry<TKey, THandler> registry,
            TKey key)
            where TKey : notnull
            where THandler : class
        {
            return registry.Get(key) != null;
        }
    }
}

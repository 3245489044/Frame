
namespace BasicMethod.Functor
{
    public static class FunctorExtensions
    {

        public static T GetOrSet<T>(this IFunctor functor, Func<T> factory) where T : class
        {
            var value = functor.Get<T>();
            if (value == null)
            {
                value = factory();
                functor.Set(value);
            }
            return value;
        }

        public static bool TryGet<T>(this IFunctor functor, out T? value) where T : class
        {
            value = functor.Get<T>();
            return value != null;
        }

        public static T? GetAs<T>(this IFunctor functor, string key) where T : class
            => functor.Get(key) as T;


        public static IFunctorScope CreateChildScope(this IFunctorScope scope, Action<IFunctor> initialize)
        {
            var child = scope.CreateScope();
            initialize(child);
            return child;
        }

        public static void WithScope(this IFunctorScope scope, Action<IFunctor> action)
        {
            using var child = scope.CreateScope();
            action(child);
        }

        public static async Task WithScopeAsync(this IFunctorScope scope, Func<IFunctor, Task> action)
        {
            using var child = scope.CreateScope();
            await action(child).ConfigureAwait(false);
        }

        public static T WithScope<T>(this IFunctorScope scope, Func<IFunctor, T> action)
        {
            using var child = scope.CreateScope();
            return action(child);
        }


        public static void SetAll(this IFunctor functor, params (string Key, object? Value)[] items)
        {
            foreach (var (key, value) in items)
                functor.Set(key, value);
        }

        public static Dictionary<string, object?> GetAll(this IFunctor functor, params string[] keys)
        {
            var result = new Dictionary<string, object?>();
            foreach (var key in keys)
                result[key] = functor.Get(key);
            return result;
        }


        public static bool TrySet<T>(this IFunctor functor, T value) where T : class
        {
            if (functor.Has<T>())
                return false;
            functor.Set(value);
            return true;
        }

        public static bool TrySet(this IFunctor functor, string key, object? value)
        {
            if (functor.Has(key))
                return false;
            functor.Set(key, value);
            return true;
        }


        public static void CopyTo(this IFunctor source, IFunctor destination, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = source.Get(key);
                if (value != null)
                    destination.Set(key, value);
            }
        }

        public static void CopyAllTo(this IFunctorScope source, IFunctor destination)
        {
            foreach (var kvp in source.Snapshot())
                destination.Set(kvp.Key, kvp.Value);
        }
    }
}

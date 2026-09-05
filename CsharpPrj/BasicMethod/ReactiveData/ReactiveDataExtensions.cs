
#nullable enable

using System.Reactive.Linq;
using Modules.ReactiveData.Abstractions;

namespace Modules.ReactiveData
{
    public static class ReactiveDataExtensions
    {
        public static IDisposable SubscribeToKey(
            this IReactiveDataStore store,
            string key,
            Action<DataChangedEventArgs> handler)
        {
            return store.ObserveKey(key).Subscribe(handler);
        }

        public static IDisposable SubscribeToType<T>(
            this IReactiveDataStore store,
            Action<DataChangedEventArgs> handler)
        {
            return store.ObserveType<T>().Subscribe(handler);
        }

        public static IDisposable SubscribeToChangeType(
            this IReactiveDataStore store,
            DataChangeType changeType,
            Action<DataChangedEventArgs> handler)
        {
            return store.DataChanged
                .Where(e => e.ChangeType == changeType)
                .Subscribe(handler);
        }

        public static IDisposable OnAdded(
            this IReactiveDataStore store,
            Action<DataChangedEventArgs> handler)
        {
            return store.SubscribeToChangeType(DataChangeType.Added, handler);
        }

        public static IDisposable OnUpdated(
            this IReactiveDataStore store,
            Action<DataChangedEventArgs> handler)
        {
            return store.SubscribeToChangeType(DataChangeType.Updated, handler);
        }

        public static IDisposable OnRemoved(
            this IReactiveDataStore store,
            Action<DataChangedEventArgs> handler)
        {
            return store.SubscribeToChangeType(DataChangeType.Removed, handler);
        }

        public static IObservable<TResult> Select<TResult>(
            this IReactiveDataStore store,
            Func<DataChangedEventArgs, TResult> selector)
        {
            return store.DataChanged.Select(selector);
        }

        public static IObservable<DataChangedEventArgs> Throttle(
            this IReactiveDataStore store,
            TimeSpan dueTime)
        {
            return store.DataChanged.Throttle(dueTime);
        }

        public static IObservable<DataChangedEventArgs> Debounce(
            this IReactiveDataStore store,
            TimeSpan dueTime)
        {
            return store.DataChanged.Throttle(dueTime);
        }

        public static IDisposable ConnectReactorPipeline(
            this IReactiveDataStore store,
            ReactorPipeline pipeline)
        {
            return pipeline.ConnectTo(store);
        }

        public static ReactiveDataStore WithInitialData(
            this ReactiveDataStore store,
            params (string Key, object Value)[] initialData)
        {
            foreach (var (key, value) in initialData)
            {
                store.Set(key, value);
            }
            return store;
        }
    }
}

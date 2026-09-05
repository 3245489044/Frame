
#nullable enable

using Modules.ReactiveData.Abstractions;

namespace Modules.ReactiveData
{
    internal class ReactorWrapper : IReactor<object>
    {
        private readonly object _inner;
        private readonly Type _dataType;

        public float Priority { get; }

        public ReactorWrapper(object inner, Type dataType, float priority)
        {
            _inner = inner;
            _dataType = dataType;
            Priority = priority;
        }

        public bool ShouldReact(object data)
        {
            if (!_dataType.IsInstanceOfType(data))
                return false;

            var shouldReactMethod = _inner.GetType().GetMethod("ShouldReact");
            if (shouldReactMethod != null)
            {
                var result = shouldReactMethod.Invoke(_inner, [data]);
                return result is bool b && b;
            }
            return false;
        }

        public void React(object data)
        {
            if (!_dataType.IsInstanceOfType(data))
                return;

            var reactMethod = _inner.GetType().GetMethod("React", [_dataType]);
            reactMethod?.Invoke(_inner, [data]);
        }

        void IReactor<object>.React(object data)
        {
            React(data);
        }
    }

    public class ReactorPipeline
    {
        private readonly List<(float Priority, IReactor<object> Reactor)> _reactors = new();
        private readonly object _lock = new();

        public void Register<T>(IReactor<T> reactor)
        {
            var wrapper = new ReactorWrapper(reactor, typeof(T), reactor.Priority);
            lock (_lock)
            {
                _reactors.Add((reactor.Priority, wrapper));
                _reactors.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public void Unregister<T>(IReactor<T> reactor)
        {
            lock (_lock)
            {
                _reactors.RemoveAll(r =>
                    r.Reactor is ReactorWrapper wrapper &&
                    ReferenceEquals(wrapper.GetInner(), reactor));
            }
        }

        public void Execute(object data)
        {
            if (data == null) return;

            List<(float Priority, IReactor<object> Reactor)> snapshot;
            lock (_lock)
            {
                snapshot = new List<(float, IReactor<object>)>(_reactors);
            }

            foreach (var (_, reactor) in snapshot)
            {
                try
                {
                    if (reactor.ShouldReact(data))
                    {
                        reactor.React(data);
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

        public void ExecuteAll(IEnumerable<object> dataItems)
        {
            if (dataItems == null) return;

            foreach (var data in dataItems)
            {
                Execute(data);
            }
        }

        public IDisposable ConnectTo(IReactiveDataStore store)
        {
            return store.DataChanged.Subscribe(e =>
            {
                if (e.NewValue != null)
                    Execute(e.NewValue);
            });
        }

        public void Clear()
        {
            lock (_lock)
            {
                _reactors.Clear();
            }
        }

        public int Count
        {
            get { lock (_lock) return _reactors.Count; }
        }
    }

    internal static class ReactorWrapperExtensions
    {
        public static object? GetInner(this ReactorWrapper wrapper)
        {
            var field = typeof(ReactorWrapper).GetField("_inner",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(wrapper);
        }
    }
}

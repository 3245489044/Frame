
using System.Runtime.CompilerServices;

namespace UniversalMiddleware.EventPipeline
{
    public class EventSuppressor : IEventSuppressor
    {
        private readonly ConditionalWeakTable<object, SuppressionCounter> _suppressionCounts = new();

        public IDisposable Suppress(object control)
        {
            ArgumentNullException.ThrowIfNull(control);

            var counter = _suppressionCounts.GetOrCreateValue(control);
            counter.Increment();

            return new SuppressionHandle(() => Release(control));
        }

        public IDisposable SuppressAll(IEnumerable<object> controls)
        {
            ArgumentNullException.ThrowIfNull(controls);

            var handles = new List<IDisposable>();
            foreach (var control in controls)
            {
                if (control != null)
                    handles.Add(Suppress(control));
            }

            return new CompositeDisposable(handles);
        }

        public bool IsSuppressed(object control)
        {
            return control != null && _suppressionCounts.TryGetValue(control, out var counter) && counter.Count > 0;
        }

        private void Release(object control)
        {
            if (_suppressionCounts.TryGetValue(control, out var counter))
            {
                counter.Decrement();
            }
        }

        private class SuppressionCounter
        {
            public int Count { get; private set; }
            public void Increment() => Count++;
            public void Decrement()
            {
                if (Count > 0) Count--;
            }
        }
    }

    internal class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables;

        public CompositeDisposable(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var d in _disposables)
                d.Dispose();
            _disposables.Clear();
        }
    }
}



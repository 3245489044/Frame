
#nullable enable

namespace Modules.GraphProcessing.Normalization
{
    public class GraphChangeEventArgs : EventArgs
    {
        public GraphChangeType ChangeType { get; }

        public string Description { get; }

        public DateTime Timestamp { get; }

        public GraphChangeEventArgs(GraphChangeType changeType, string description)
        {
            ChangeType = changeType;
            Description = description;
            Timestamp = DateTime.UtcNow;
        }
    }

    public enum GraphChangeType
    {
        NodeAdded,
        NodeRemoved,
        NodeUpdated,
        EdgeAdded,
        EdgeRemoved,
        EdgeUpdated,
        GraphCleared,
        Normalized,
        Reset
    }

    public interface IObservableGraph
    {
        event EventHandler<GraphChangeEventArgs>? GraphChanged;

        void NotifyGraphChanged(GraphChangeEventArgs args);
    }

    public static class GraphObservableExtensions
    {
        public static IDisposable SubscribeTo(
            this IObservableGraph observable,
            GraphChangeType changeType,
            EventHandler<GraphChangeEventArgs> handler)
        {
            void WrappedHandler(object? sender, GraphChangeEventArgs e)
            {
                if (e.ChangeType == changeType)
                    handler(sender, e);
            }

            observable.GraphChanged += WrappedHandler;

            return new Unsubscriber(() => observable.GraphChanged -= WrappedHandler);
        }

        public static IDisposable SubscribeAll(
            this IObservableGraph observable,
            EventHandler<GraphChangeEventArgs> handler)
        {
            observable.GraphChanged += handler;
            return new Unsubscriber(() => observable.GraphChanged -= handler);
        }

        public static IDisposable SubscribeNodeChanges(
            this IObservableGraph observable,
            EventHandler<GraphChangeEventArgs> handler)
        {
            void WrappedHandler(object? sender, GraphChangeEventArgs e)
            {
                if (e.ChangeType is GraphChangeType.NodeAdded
                    or GraphChangeType.NodeRemoved
                    or GraphChangeType.NodeUpdated)
                    handler(sender, e);
            }

            observable.GraphChanged += WrappedHandler;
            return new Unsubscriber(() => observable.GraphChanged -= WrappedHandler);
        }

        public static IDisposable SubscribeEdgeChanges(
            this IObservableGraph observable,
            EventHandler<GraphChangeEventArgs> handler)
        {
            void WrappedHandler(object? sender, GraphChangeEventArgs e)
            {
                if (e.ChangeType is GraphChangeType.EdgeAdded
                    or GraphChangeType.EdgeRemoved
                    or GraphChangeType.EdgeUpdated)
                    handler(sender, e);
            }

            observable.GraphChanged += WrappedHandler;
            return new Unsubscriber(() => observable.GraphChanged -= WrappedHandler);
        }

    }

    internal class Unsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Unsubscriber(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _unsubscribe();
                _disposed = true;
            }
        }
    }

}

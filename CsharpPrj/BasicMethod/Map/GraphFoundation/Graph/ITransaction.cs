
#nullable enable

using GraphFoundation.Storage;

namespace GraphFoundation.Abstractions
{
    public interface ICommand
    {
        void Execute(TransactionContextBase context);

        void Undo(TransactionContextBase context);
    }

    public abstract class TransactionContextBase
    {
        internal List<GraphEvent> PendingEvents { get; } = new();
    }

    public class TransactionContext<TKey, TNodeData, TEdgeData> : TransactionContextBase
        where TKey : notnull
        where TNodeData : class
    {
        public IGraphStorage<TKey, TNodeData, TEdgeData> Storage { get; }

        public TransactionContext(IGraphStorage<TKey, TNodeData, TEdgeData> storage)
        {
            Storage = storage;
        }
    }

    public interface ITransaction : IDisposable
    {
        void Enqueue(ICommand command);

        void Commit();

        void Rollback();

        bool IsActive { get; }

        void OnCommit(Action callback);
    }
}

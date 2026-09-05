
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Storage;

namespace GraphFoundation.Core.Commands
{
    public abstract class CommandBase<TKey, TNodeData, TEdgeData> : ICommand
        where TKey : notnull
        where TNodeData : class
    {
        protected abstract void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx);
        protected abstract void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx);

        public void Execute(TransactionContextBase context)
        {
            var typedCtx = (TransactionContext<TKey, TNodeData, TEdgeData>)context;
            ExecuteCore(typedCtx);
        }

        public void Undo(TransactionContextBase context)
        {
            var typedCtx = (TransactionContext<TKey, TNodeData, TEdgeData>)context;
            UndoCore(typedCtx);
        }
    }

    public class AddNodeCommand<TKey, TNodeData, TEdgeData> : CommandBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly TKey _key;
        private readonly TNodeData _data;
        private readonly string? _symbol;

        public AddNodeCommand(TKey key, TNodeData data, string? symbol = null)
        {
            _key = key;
            _data = data;
            _symbol = symbol;
        }

        protected override void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryAddNode(_key, _data, _symbol);
            ctx.PendingEvents.Add(new NodeAddedEvent<TKey>(_key, _data, _symbol));
        }

        protected override void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryRemoveNode(_key, out _, out _);
        }
    }

    public class RemoveNodeCommand<TKey, TNodeData, TEdgeData> : CommandBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly TKey _key;
        private TNodeData? _oldData;
        private string? _oldSymbol;

        public RemoveNodeCommand(TKey key) => _key = key;

        protected override void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryRemoveNode(_key, out _oldData, out _oldSymbol);
            ctx.PendingEvents.Add(new NodeRemovedEvent<TKey>(_key, _oldData, _oldSymbol));
        }

        protected override void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            if (_oldData != null)
                ctx.Storage.TryAddNode(_key, _oldData, _oldSymbol);
        }
    }


    public class UpdateNodeCommand<TKey, TNodeData, TEdgeData> : CommandBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly TKey _key;
        private readonly TNodeData _data;
        private readonly string? _newSymbol;
        private TNodeData? _oldData;
        private string? _oldSymbol;

        public UpdateNodeCommand(TKey key, TNodeData data, string? newSymbol = null)
        {
            _key = key;
            _data = data;
            _newSymbol = newSymbol;
        }

        protected override void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryUpdateNode(_key, _data, _newSymbol, out _oldData, out _oldSymbol);
            ctx.PendingEvents.Add(new NodeUpdatedEvent<TKey>(_key, _oldData, _data, _oldSymbol, _newSymbol));
        }

        protected override void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            if (_oldData != null)
                ctx.Storage.TryUpdateNode(_key, _oldData, _oldSymbol, out _, out _);
        }
    }

    public class AddEdgeCommand<TKey, TNodeData, TEdgeData> : CommandBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly string _edgeId;
        private readonly TKey _source, _target;
        private readonly bool _isDirected;
        private readonly TEdgeData? _data;
        private readonly double _weight;

        public AddEdgeCommand(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0)
        {
            _edgeId = edgeId;
            _source = source;
            _target = target;
            _isDirected = isDirected;
            _data = data;
            _weight = weight;
        }

        protected override void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryAddEdge(_edgeId, _source, _target, _isDirected, _data, _weight);
            ctx.PendingEvents.Add(new EdgeAddedEvent<TKey>(_edgeId, _source, _target, _isDirected, _weight, _data));
        }

        protected override void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryRemoveEdge(_edgeId, out _);
        }
    }

    public class RemoveEdgeCommand<TKey, TNodeData, TEdgeData> : CommandBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly string _edgeId;
        private EdgeSnapshot<TKey, TEdgeData> _snapshot;

        public RemoveEdgeCommand(string edgeId) => _edgeId = edgeId;

        protected override void ExecuteCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryGetEdge(_edgeId, out _snapshot);
            ctx.Storage.TryRemoveEdge(_edgeId, out _);
            ctx.PendingEvents.Add(new EdgeRemovedEvent<TKey>(_edgeId, _snapshot.Source, _snapshot.Target));
        }

        protected override void UndoCore(TransactionContext<TKey, TNodeData, TEdgeData> ctx)
        {
            ctx.Storage.TryAddEdge(_edgeId, _snapshot.Source, _snapshot.Target,
                                   _snapshot.IsDirected, _snapshot.Data, _snapshot.Weight);
        }
    }
}

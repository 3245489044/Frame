
#nullable enable

using ECS.Core;
using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Infrastructure.Graph;
using Modules.GraphProcessing.Normalization;

namespace Modules.GraphProcessing.Infrastructure.Events
{
    public class GraphChangedEventArgs : EventArgs
    {
        public GraphChangeType ChangeType { get; }

        public object? NodeKey { get; }

        public string? EdgeId { get; }

        public GraphChangedEventArgs(GraphChangeType changeType, object? nodeKey = null, string? edgeId = null)
        {
            ChangeType = changeType;
            NodeKey = nodeKey;
            EdgeId = edgeId;
        }
    }

    public class GraphEventAdapter<TKey, TNodeData, TEdgeData> : IDisposable
        where TKey : notnull
        where TNodeData : class
    {
        private readonly EcsEventBus _eventBus;
        private readonly EntityMap<TKey> _entityMap;
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private bool _disposed;

        public event EventHandler<GraphChangedEventArgs>? GraphChanged;

        public GraphEventAdapter(
            EcsEventBus eventBus,
            EntityMap<TKey> entityMap,
            ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _eventBus = eventBus;
            _entityMap = entityMap;
            _graph = graph;

            _eventBus.Subscribe<EntityDestroyedEventArgs>(OnEntityDestroyed);
            _eventBus.Subscribe<ComponentChangedEventArgs>(OnComponentChanged);
        }

        private void OnEntityDestroyed(EntityDestroyedEventArgs args)
        {
            if (args.Entity.HasValue && _entityMap.TryGetKey(args.Entity.Value, out var key))
            {
                GraphChanged?.Invoke(this,
                    new GraphChangedEventArgs(GraphChangeType.NodeRemoved, nodeKey: key));
            }
        }

        private void OnComponentChanged(ComponentChangedEventArgs args)
        {
            if (!args.Entity.HasValue) return;
            var entity = args.Entity.Value;

            if (!_entityMap.TryGetKey(entity, out var key)) return;

            GraphChangeType changeType;
            if (args.ComponentType == typeof(NodeDataComponent<TNodeData>))
            {
                changeType = args.ChangeType switch
                {
                    "Added" => GraphChangeType.NodeAdded,
                    "Removed" => GraphChangeType.NodeRemoved,
                    "Updated" => GraphChangeType.NodeUpdated,
                    _ => GraphChangeType.NodeUpdated
                };
            }
            else if (args.ComponentType.Name.StartsWith("EdgeComponent"))
            {
                changeType = args.ChangeType switch
                {
                    "Added" => GraphChangeType.EdgeAdded,
                    "Removed" => GraphChangeType.EdgeRemoved,
                    "Updated" => GraphChangeType.EdgeUpdated,
                    _ => GraphChangeType.EdgeUpdated
                };
            }
            else
            {
                return;
            }

            GraphChanged?.Invoke(this,
                new GraphChangedEventArgs(changeType, nodeKey: key));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _eventBus.Unsubscribe<EntityDestroyedEventArgs>(OnEntityDestroyed);
                _eventBus.Unsubscribe<ComponentChangedEventArgs>(OnComponentChanged);
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}

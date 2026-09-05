
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFoundation.Storage;

namespace Modules.GraphProcessing.Infrastructure.Graph
{
    public class GraphComponentStore<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IGraphStorage<TKey, TNodeData, TEdgeData> _storage;
        private readonly EntityManager _entityManager;
        private readonly EntityMap<TKey> _entityMap;

        public GraphComponentStore(
            IGraphStorage<TKey, TNodeData, TEdgeData> storage,
            EntityManager entityManager,
            EntityMap<TKey> entityMap)
        {
            _storage = storage;
            _entityManager = entityManager;
            _entityMap = entityMap;
        }

        public void SyncNodeToComponent(TKey key)
        {
            if (!_entityMap.TryGetEntity(key, out var entity))
                return;

            if (_storage.TryGetNode(key, out var data, out _) && data != null)
            {
                if (_entityManager.HasComponent<NodeDataComponent<TNodeData>>(entity))
                {
                    _entityManager.SetComponent(entity, new NodeDataComponent<TNodeData>(data));
                }
                else
                {
                    _entityManager.AddComponent(entity, new NodeDataComponent<TNodeData>(data));
                }
            }
        }

        public bool SyncComponentToNode(Entity entity)
        {
            if (!_entityMap.TryGetKey(entity, out var key))
                return false;

            if (_entityManager.TryGetComponent<NodeDataComponent<TNodeData>>(entity, out var nodeComp)
                && nodeComp.Data != null)
            {
                if (_storage.ContainsNode(key))
                {
                    _storage.TryUpdateNode(key, nodeComp.Data, null, out var oldData, out var oldSymbol);
                }
                else
                {
                    _storage.TryAddNode(key, nodeComp.Data, null);
                }
                return true;
            }
            return false;
        }

        public int SyncAllToGraph()
        {
            int count = 0;
            foreach (var entity in _entityMap.GetAllEntities())
            {
                if (SyncComponentToNode(entity))
                    count++;
            }
            return count;
        }

        public int SyncAllToEcs()
        {
            int count = 0;
            foreach (var key in _storage.GetAllNodeKeys())
            {
                SyncNodeToComponent(key);
                count++;
            }
            return count;
        }
    }
}

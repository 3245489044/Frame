
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;

namespace Modules.GraphProcessing.Infrastructure.Graph
{
    public class EntityMap<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, Entity> _keyToEntity = new();
        private readonly Dictionary<Entity, TKey> _entityToKey = new();
        private readonly Func<TKey, string> _keyToString;

        public EntityMap(Func<TKey, string>? keyToString = null)
        {
            _keyToString = keyToString ?? (k => k.ToString() ?? k.GetHashCode().ToString());
        }

        public void Add(TKey key, Entity entity)
        {
            _keyToEntity[key] = entity;
            _entityToKey[entity] = key;
        }

        public bool Remove(TKey key)
        {
            if (_keyToEntity.TryGetValue(key, out var entity))
            {
                _keyToEntity.Remove(key);
                _entityToKey.Remove(entity);
                return true;
            }
            return false;
        }

        public bool Remove(Entity entity)
        {
            if (_entityToKey.TryGetValue(entity, out var key))
            {
                _entityToKey.Remove(entity);
                _keyToEntity.Remove(key);
                return true;
            }
            return false;
        }

        public bool TryGetEntity(TKey key, out Entity entity)
            => _keyToEntity.TryGetValue(key, out entity);

        public bool TryGetKey(Entity entity, out TKey key)
            => _entityToKey.TryGetValue(entity, out key);

        public string GetEntityId(TKey key)
            => _keyToString(key);

        public IEnumerable<TKey> GetAllKeys() => _keyToEntity.Keys;
        public IEnumerable<Entity> GetAllEntities() => _entityToKey.Keys;
        public int Count => _keyToEntity.Count;
        public void Clear() { _keyToEntity.Clear(); _entityToKey.Clear(); }
    }

    public struct NodeDataComponent<TNodeData> : IComponent
        where TNodeData : class
    {
        public TNodeData? Data { get; }

        public NodeDataComponent(TNodeData? data)
        {
            Data = data;
        }
    }

    public struct EdgeComponent<TKey, TEdgeData> : IComponent
    {
        public string EdgeId { get; }
        public TKey Source { get; }
        public TKey Target { get; }
        public TEdgeData? Data { get; }
        public double Weight { get; }

        public EdgeComponent(string edgeId, TKey source, TKey target, TEdgeData? data, double weight)
        {
            EdgeId = edgeId;
            Source = source;
            Target = target;
            Data = data;
            Weight = weight;
        }
    }

    public class GraphEntityAdapter<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly EntityManager _entityManager;
        private readonly EntityMap<TKey> _entityMap;

        public EntityMap<TKey> EntityMap => _entityMap;

        public EntityManager EntityManager => _entityManager;

        public GraphEntityAdapter(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            EntityManager entityManager,
            Func<TKey, string>? keyToString = null)
        {
            _graph = graph;
            _entityManager = entityManager;
            _entityMap = new EntityMap<TKey>(keyToString);
        }

        public Entity SyncNodeToEntity(TKey key)
        {
            if (_entityMap.TryGetEntity(key, out var existing))
            {
                var nodeData = _graph.GetNodeData(key);
                if (nodeData != null)
                {
                    _entityManager.SetComponent(existing, new NodeDataComponent<TNodeData>(nodeData));
                }
                return existing;
            }

            var entity = _entityManager.CreateEntity();
            _entityMap.Add(key, entity);

            var data = _graph.GetNodeData(key);
            if (data != null)
            {
                _entityManager.AddComponent(entity, new NodeDataComponent<TNodeData>(data));
            }

            return entity;
        }

        public List<Entity> SyncAllNodesToEntities()
        {
            var entities = new List<Entity>();
            foreach (var key in _graph.GetAllNodeKeys())
            {
                entities.Add(SyncNodeToEntity(key));
            }
            return entities;
        }

        public bool SyncEntityToNode(Entity entity)
        {
            if (!_entityMap.TryGetKey(entity, out var key))
                return false;

            if (_entityManager.TryGetComponent<NodeDataComponent<TNodeData>>(entity, out var nodeComponent))
            {
                if (_graph.ContainsNode(key))
                {
                    _graph.UpdateNode(key, nodeComponent.Data);
                }
                else
                {
                    _graph.AddNode(key, nodeComponent.Data);
                }
                return true;
            }
            return false;
        }

        public int SyncAllEntitiesToGraph()
        {
            int count = 0;
            foreach (var entity in _entityMap.GetAllEntities())
            {
                if (SyncEntityToNode(entity))
                    count++;
            }
            return count;
        }

        public void SyncEdgeToEntity(string edgeId)
        {
            if (!_graph.ContainsEdge(edgeId)) return;

            var source = _graph.GetEdgeSource(edgeId);
            var target = _graph.GetEdgeTarget(edgeId);
            var data = _graph.GetEdgeData(edgeId);
            var weight = _graph.GetEdgeWeight(edgeId);

            if (_entityMap.TryGetEntity(source, out var sourceEntity))
            {
                _entityManager.AddComponent(sourceEntity,
                    new EdgeComponent<TKey, TEdgeData>(edgeId, source, target, data, weight));
            }
        }

        public void DestroyEntity(TKey key)
        {
            if (_entityMap.TryGetEntity(key, out var entity))
            {
                _entityManager.DestroyEntity(entity);
                _entityMap.Remove(key);
            }
        }

        public void Clear()
        {
            _entityMap.Clear();
        }
    }
}

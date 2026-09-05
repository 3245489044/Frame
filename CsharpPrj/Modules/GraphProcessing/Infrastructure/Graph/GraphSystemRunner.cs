
#nullable enable

using ECS.Core;
using GraphFoundation.Abstractions;
using TConfig.ContextFrame.Abstractions;

namespace Modules.GraphProcessing.Infrastructure.Graph
{
    public class GraphSystemRunner<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly List<SystemBase> _systems = new();
        private readonly object _lock = new();
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData>? _graph;
        private GraphEntityAdapter<TKey, TNodeData, TEdgeData>? _adapter;

        public EcsWorld World { get; }

        public ICoreGraph<TKey, TNodeData, TEdgeData> Graph => _graph ?? throw new InvalidOperationException("Graph is not set on this runner.");

        public GraphEntityAdapter<TKey, TNodeData, TEdgeData> Adapter => _adapter ?? throw new InvalidOperationException("Adapter has not been initialized. Call Initialize() first.");

        public int SystemCount
        {
            get { lock (_lock) return _systems.Count; }
        }

        public GraphSystemRunner(
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            EcsWorld? world = null)
        {
            World = world ?? new EcsWorld(ecsStorage, storageRegistry);
        }

        public GraphSystemRunner(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            Func<TKey, string>? keyToString = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            World = new EcsWorld(ecsStorage, storageRegistry);
            _adapter = new GraphEntityAdapter<TKey, TNodeData, TEdgeData>(
                _graph, World.EntityManager,
                keyToString ?? (k => k?.ToString() ?? k!.GetHashCode().ToString()));
        }

        public void Initialize()
        {
            if (_graph == null)
                throw new InvalidOperationException("Cannot initialize without a graph. Use the constructor that accepts ICoreGraph.");

            _adapter = new GraphEntityAdapter<TKey, TNodeData, TEdgeData>(
                _graph, World.EntityManager,
                k => k?.ToString() ?? k!.GetHashCode().ToString());
        }

        public GraphSystemRunner<TKey, TNodeData, TEdgeData> Register(SystemBase system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            lock (_lock)
            {
                system.Initialize(World.EntityManager);
                _systems.Add(system);
            }

            return this;
        }

        public GraphSystemRunner<TKey, TNodeData, TEdgeData> RegisterRange(IEnumerable<SystemBase> systems)
        {
            if (systems == null)
                throw new ArgumentNullException(nameof(systems));

            lock (_lock)
            {
                foreach (var system in systems)
                {
                    system.Initialize(World.EntityManager);
                    _systems.Add(system);
                }
            }

            return this;
        }

        public bool Unregister(SystemBase system)
        {
            lock (_lock)
            {
                return _systems.Remove(system);
            }
        }

        private List<SystemBase> GetSystemsSnapshot()
        {
            lock (_lock)
            {
                return _systems.ToList();
            }
        }

        public void Execute(float deltaTime = 0)
        {
            foreach (var system in GetSystemsSnapshot())
            {
                system.Update(deltaTime);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _systems.Clear();
            }
        }
    }
}

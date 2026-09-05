
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFoundation.Topology;
using GraphFramework.Helpers;

namespace GraphFramework.Ecs.Systems
{
    public struct TopologyResultComponent : IComponent
    {
        public string[] SortedNodeKeys { get; }

        public bool HasCycle { get; }

        public string[]? CycleNodeKeys { get; }

        public TopologyResultComponent(string[] sortedNodeKeys, bool hasCycle = false, string[]? cycleNodeKeys = null)
        {
            SortedNodeKeys = sortedNodeKeys;
            HasCycle = hasCycle;
            CycleNodeKeys = cycleNodeKeys;
        }
    }

    public class TopologySystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly Func<TKey, string> _keyToString;

        public TopologySystem(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<TKey, string>? keyToString = null)
        {
            _graph = graph;
            _keyToString = keyToString ?? (k => k.ToString() ?? k.GetHashCode().ToString());
        }

        public override void Update(float deltaTime)
        {
            var result = GraphTopology.TopologicalSort(_graph);

            var sortedKeys = result.SortedNodes
                .Select(k => _keyToString(k))
                .ToArray();

            string[]? cycleKeys = result.CycleNodes?
                .Select(k => _keyToString(k))
                .ToArray();

            EntityHelper.SetOrAddComponent(EntityManager,
                EntityHelper.FindOrCreateEntityWithComponent<TopologyResultComponent>(EntityManager),
                new TopologyResultComponent(sortedKeys, result.HasCycle, cycleKeys));
        }
    }
}



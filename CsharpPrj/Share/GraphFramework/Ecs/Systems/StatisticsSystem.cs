
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFoundation.ErrorHandling;
using GraphFoundation.Topology;
using GraphFramework.Helpers;

namespace GraphFramework.Ecs.Systems
{
    public struct GraphStatsComponent : IComponent
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
        public double AvgOutDegree { get; set; }
        public double AvgInDegree { get; set; }
        public double AvgWeight { get; set; }
        public int IsolatedNodeCount { get; set; }
        public int ConnectedComponentCount { get; set; }
        public string[][]? ConnectedComponents { get; set; }
        public bool HasCycle { get; set; }

        public override string ToString()
            => $"Stats: Nodes={NodeCount}, Edges={EdgeCount}, " +
               $"AvgOutDegree={AvgOutDegree:F2}, AvgInDegree={AvgInDegree:F2}, " +
               $"AvgWeight={AvgWeight:F2}, Isolated={IsolatedNodeCount}, " +
               $"Components={ConnectedComponentCount}, HasCycle={HasCycle}";
    }

    public class StatisticsSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;

        public StatisticsSystem(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public override void Update(float deltaTime)
        {
            var stats = new GraphStatistics<TKey, TNodeData, TEdgeData>(_graph);
            var result = stats.Compute();

            var components = ComputeConnectedComponents();

            EntityHelper.SetOrAddComponent(EntityManager,
                EntityHelper.FindOrCreateEntityWithComponent<GraphStatsComponent>(EntityManager),
                new GraphStatsComponent
                {
                    NodeCount = result.NodeCount,
                    EdgeCount = result.EdgeCount,
                    AvgOutDegree = result.AvgOutDegree,
                    AvgInDegree = result.AvgInDegree,
                    AvgWeight = result.AvgWeight,
                    IsolatedNodeCount = result.IsolatedNodeCount,
                    ConnectedComponentCount = result.ConnectedComponentCount,
                    ConnectedComponents = components,
                    HasCycle = result.HasCycle
                });
        }

        private string[][]? ComputeConnectedComponents()
        {
            var components = GraphTopology.GetConnectedComponents(_graph);
            if (components.Count == 0) return null;

            return components
                .Select(c => c.Select(k => k.ToString() ?? k.GetHashCode().ToString()).ToArray())
                .ToArray();
        }
    }
}



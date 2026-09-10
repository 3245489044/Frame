
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFoundation.Chain;
using GraphFoundation.Core;
using GraphFramework.Helpers;

namespace GraphFramework.Ecs.Systems
{
    public struct ChainDataComponent<T> : IComponent
    {
        public string Name { get; }

        public T[] Nodes { get; }

        public ChainDataComponent(string name, T[] nodes)
        {
            Name = name;
            Nodes = nodes;
        }
    }

    public struct ChainProcessingResultComponent : IComponent
    {
        public int ChainCount { get; }
        public int NodeCount { get; }
        public int EdgeCount { get; }

        public ChainProcessingResultComponent(int chainCount, int nodeCount, int edgeCount)
        {
            ChainCount = chainCount;
            NodeCount = nodeCount;
            EdgeCount = edgeCount;
        }
    }

    public class ChainSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly Func<string, TKey> _keyFactory;
        private readonly Func<TKey, TNodeData> _nodeDataFactory;
        private readonly Func<TKey, TKey, TEdgeData> _edgeDataFactory;
        private readonly ChainProcessor<TKey, TNodeData, TEdgeData> _processor;

        public ChainSystem(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<string, TKey> keyFactory,
            Func<TKey, TNodeData> nodeDataFactory,
            Func<TKey, TKey, TEdgeData> edgeDataFactory)
        {
            _graph = graph;
            _keyFactory = keyFactory;
            _nodeDataFactory = nodeDataFactory;
            _edgeDataFactory = edgeDataFactory;
            _processor = new ChainProcessor<TKey, TNodeData, TEdgeData>(
                _graph, _keyFactory, _nodeDataFactory, _edgeDataFactory);
        }

        public override void Update(float deltaTime)
        {
            var chainEntities = new List<(Entity Entity, ChainDataComponent<TKey> Component)>();

            foreach (var entity in EntityManager.GetAllEntities())
            {
                if (EntityManager.TryGetComponent<ChainDataComponent<TKey>>(entity, out var chainComp))
                {
                    chainEntities.Add((entity, chainComp));
                }
            }

            if (chainEntities.Count == 0) return;

            int totalChains = 0, totalNodes = 0, totalEdges = 0;
            var chainCollection = new ChainCollection<TKey>();
            foreach (var (_, chainComp) in chainEntities)
            {
                var addedChain = chainCollection.Add(chainComp.Name);
                foreach (var node in chainComp.Nodes)
                {
                    addedChain.Add(node);
                }
                totalChains++;
                totalNodes += chainComp.Nodes.Length;
            }

            if (totalChains > 0)
            {
                var singleResult = _processor.ProcessChains(chainCollection);
                totalEdges = singleResult.EdgeCount;
            }

            Entity resultEntity = EntityHelper.FindOrCreateEntityWithComponent<ChainProcessingResultComponent>(EntityManager);
            EntityHelper.SetOrAddComponent(EntityManager, resultEntity,
                new ChainProcessingResultComponent(totalChains, totalNodes, totalEdges));
        }
    }
}



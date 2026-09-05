
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFramework.Helpers;
using GraphOrchestration = Modules.GraphProcessing.Orchestration;

namespace GraphFramework.Ecs.Systems
{
    public struct OrchestrationResultComponent : IComponent
    {
        public int ChainsCreated { get; }

        public int ChainsMerged { get; }

        public int NodesCreated { get; }

        public int EdgesCreated { get; }

        public OrchestrationResultComponent(int chainsCreated, int chainsMerged, int nodesCreated, int edgesCreated)
        {
            ChainsCreated = chainsCreated;
            ChainsMerged = chainsMerged;
            NodesCreated = nodesCreated;
            EdgesCreated = edgesCreated;
        }
    }

    public class OrchestrationSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly GraphOrchestration.OrchestrationService<TKey, TNodeData, TEdgeData> _service;
        private readonly TNodeData _template;
        private readonly int _chainCount;
        private readonly int _nodesPerChain;
        private readonly GraphOrchestration.IKeyGenerator<TKey>? _keyGenerator;

        public OrchestrationSystem(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            TNodeData template,
            int chainCount,
            int nodesPerChain,
            GraphOrchestration.IKeyGenerator<TKey>? keyGenerator = null,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null)
        {
            _service = new GraphOrchestration.OrchestrationService<TKey, TNodeData, TEdgeData>(
                graph, keyExtractor, setPosition, cloner);
            _template = template;
            _chainCount = chainCount;
            _nodesPerChain = nodesPerChain;
            _keyGenerator = keyGenerator;
        }

        public OrchestrationSystem(
            GraphOrchestration.OrchestrationService<TKey, TNodeData, TEdgeData> service,
            TNodeData template,
            int chainCount,
            int nodesPerChain,
            GraphOrchestration.IKeyGenerator<TKey>? keyGenerator = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _template = template;
            _chainCount = chainCount;
            _nodesPerChain = nodesPerChain;
            _keyGenerator = keyGenerator;
        }

        public override void Update(float deltaTime)
        {
            var result = _service.CreateMultipleChains(_template, _chainCount, _nodesPerChain, _keyGenerator);

            EntityHelper.SetOrAddComponent(EntityManager, 
                EntityHelper.FindOrCreateEntityWithComponent<OrchestrationResultComponent>(EntityManager),
                new OrchestrationResultComponent(
                    result.ChainsCreated,
                    result.ChainsMerged,
                    result.NodesCreated,
                    result.EdgesCreated));
        }

        public static OrchestrationResultComponent? GetResult(EcsWorld world)
        {
            foreach (var entity in world.EntityManager.GetAllEntities())
            {
                if (world.EntityManager.TryGetComponent<OrchestrationResultComponent>(entity, out var result))
                {
                    return result;
                }
            }
            return null;
        }
    }
}




#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Normalization;

namespace GraphFramework.Ecs.Systems
{
    public struct NormalizationResultComponent : IComponent
    {
        public int RemovedIsolatedNodes { get; set; }

        public int MergedDuplicateEdges { get; set; }

        public int NormalizedIdentifiers { get; set; }

        public int ValidationIssues { get; set; }

        public bool IsSuccess { get; set; }

        public override string ToString()
            => $"Normalization: IsolatedRemoved={RemovedIsolatedNodes}, " +
               $"DuplicatesMerged={MergedDuplicateEdges}, " +
               $"IdentifiersNormalized={NormalizedIdentifiers}, " +
               $"ValidationIssues={ValidationIssues}, " +
               $"Success={IsSuccess}";
    }

    public class NormalizationSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly NormalizationTransformer _transformer;
        private readonly INormalizationConfig? _config;

        public NormalizationSystem(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            INormalizationConfig? config = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _config = config;
            _transformer = new NormalizationTransformer(config);
        }

        public override void Update(float deltaTime)
        {
            var result = _transformer.Execute(_graph);

            var resultComponent = new NormalizationResultComponent
            {
                RemovedIsolatedNodes = result.RemovedIsolatedNodes,
                MergedDuplicateEdges = result.MergedDuplicateEdges,
                NormalizedIdentifiers = result.NormalizedIdentifiers,
                ValidationIssues = result.ValidationIssues,
                IsSuccess = result.IsSuccess
            };

            foreach (var entity in EntityManager.GetAllEntities())
            {
                if (EntityManager.HasComponent<NormalizationResultComponent>(entity))
                {
                    EntityManager.SetComponent(entity, resultComponent);
                    return;
                }
            }

            var newEntity = EntityManager.CreateEntity();
            EntityManager.AddComponent(newEntity, resultComponent);
        }

        public static NormalizationResultComponent? GetResult(EcsWorld world)
        {
            foreach (var entity in world.EntityManager.GetAllEntities())
            {
                if (world.EntityManager.TryGetComponent<NormalizationResultComponent>(entity, out var result))
                {
                    return result;
                }
            }
            return null;
        }
    }
}



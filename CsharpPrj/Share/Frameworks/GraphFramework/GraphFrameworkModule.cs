
#nullable enable

using ECS.Core;
using GraphFoundation.Abstractions;
using GraphFramework.Ecs.Pipeline;
using GraphFramework.Pipeline;
using Modules.GraphProcessing.Orchestration;
using Modules.GraphProcessing.Reference;
using Modules.GraphProcessing.Normalization;

namespace GraphFramework.Bootstrapping
{
    public static class GraphFrameworkModule
    {

        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateGraphPipeline<TKey, TNodeData, TEdgeData>(
            string name = "default")
            where TKey : notnull
            where TNodeData : class
            => GraphFrameworkUsage.CreateGraphPipeline<TKey, TNodeData, TEdgeData>(name);

        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateFullGraphPipeline<TKey, TNodeData, TEdgeData>(
            string name = "full")
            where TKey : notnull
            where TNodeData : class
            => GraphFrameworkUsage.CreateFullGraphPipeline<TKey, TNodeData, TEdgeData>(name);

        public static GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData> CreateOrchestrator<TKey, TNodeData, TEdgeData>(
            string name = "GraphOrchestrator")
            where TKey : notnull
            where TNodeData : class
            => GraphFrameworkUsage.CreateOrchestrator<TKey, TNodeData, TEdgeData>(name);


        public static OrchestrationService<TKey, TNodeData, TEdgeData> CreateOrchestrationService<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null,
            Func<int, TKey>? keyConverter = null)
            where TKey : notnull
            where TNodeData : class
        {
            return new OrchestrationService<TKey, TNodeData, TEdgeData>(
                graph, keyExtractor, setPosition, cloner, keyConverter);
        }

        public static ReferenceService<TKey, TNodeData> CreateReferenceService<TKey, TNodeData>()
            where TKey : notnull
            where TNodeData : class
            => new ReferenceService<TKey, TNodeData>();

        public static NormalizationTransformer CreateNormalizationTransformer(
            INormalizationConfig? config = null)
            => new NormalizationTransformer(config);
    }
}

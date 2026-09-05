
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFramework.Ecs.Systems;
using Modules.GraphProcessing.Normalization;

namespace GraphFramework.Ecs.Pipeline
{
    public class NormalizeStep<TKey, TNodeData, TEdgeData> : PipelineStepBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly INormalizationConfig? _config;

        public override string Name => "Normalize";

        public NormalizeStep(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            INormalizationConfig? config = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _config = config;
        }

        public override void Execute(EcsWorld world)
        {
            var system = new NormalizationSystem<TKey, TNodeData, TEdgeData>(_graph, _config);
            system.Initialize(world.EntityManager);
            system.Update(0);
        }
    }
}



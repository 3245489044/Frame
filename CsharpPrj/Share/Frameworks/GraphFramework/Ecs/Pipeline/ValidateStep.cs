
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFramework.Ecs.Systems;

namespace GraphFramework.Ecs.Pipeline
{
    public class ValidateStep<TKey, TNodeData, TEdgeData> : PipelineStepBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;

        public override string Name => "Validate";

        public ValidateStep(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = PortGuard.Required(graph, nameof(graph));
        }

        public override void Execute(EcsWorld world)
        {
            var system = new ValidationSystem<TKey, TNodeData, TEdgeData>(_graph);
            system.Initialize(world.EntityManager);
            system.Update(0);
        }
    }
}



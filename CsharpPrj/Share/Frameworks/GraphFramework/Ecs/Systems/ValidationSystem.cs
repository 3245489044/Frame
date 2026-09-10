
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFoundation.ErrorHandling;
using GraphFramework.Helpers;

namespace GraphFramework.Ecs.Systems
{
    public struct ValidationResultComponent : IComponent
    {
        public bool IsValid { get; set; }

        public string[] Errors { get; set; }

        public string[] Warnings { get; set; }

        public ValidationResultComponent(bool isValid, string[] errors, string[] warnings)
        {
            IsValid = isValid;
            Errors = errors;
            Warnings = warnings;
        }
    }

    public class ValidationSystem<TKey, TNodeData, TEdgeData> : SystemBase
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;

        public ValidationSystem(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public override void Update(float deltaTime)
        {
            var validator = new GraphValidator<TKey, TNodeData, TEdgeData>(_graph);
            var result = validator.Validate();

            EntityHelper.SetOrAddComponent(EntityManager,
                EntityHelper.FindOrCreateEntityWithComponent<ValidationResultComponent>(EntityManager),
                new ValidationResultComponent(
                    result.IsValid,
                    result.Errors.Select(e => e.Message).ToArray(),
                    result.Warnings.ToArray()));
        }
    }
}



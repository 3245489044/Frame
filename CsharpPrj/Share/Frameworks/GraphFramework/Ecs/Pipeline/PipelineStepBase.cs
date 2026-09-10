
#nullable enable

using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs.Pipeline
{
    public abstract class PipelineStepBase
    {
        public abstract string Name { get; }

        public abstract void Execute(EcsWorld world);
    }
}



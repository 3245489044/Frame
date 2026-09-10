
#nullable enable

using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs.Pipeline
{
    public class PipelineConfig
    {
        public string Name { get; set; } = "DefaultPipeline";

        public bool EnableParallel { get; set; } = false;

        public bool StopOnError { get; set; } = true;

        public int MaxParallelism { get; set; } = 4;

        public static PipelineConfig CreateDefault()
            => new()
            {
                Name = "DefaultPipeline",
                EnableParallel = false,
                StopOnError = true,
                MaxParallelism = 4
            };
    }
}




using Modules.Pipeline;

namespace Share.StateManagement.Stages
{
    public static class StagePipelineBuilder
    {
        public static IEnumerable<IPipelineStage<IStageContext>> GetDefaultStages()
        {
            return new IPipelineStage<IStageContext>[]
            {
                new InitStage(),
                new PauseStage(),
                new ConfigStage(),
                new AdapterStage(),
                new ExecutionStage(),
                new LoopStage(),
                new RecordStage(),
                new MetricsStage(),
                new ErrorHandlingStage(),
                new FinalizeStage()
            };
        }

        public static StagePipeline<IStageContext> CreateDefaultPipeline(
            string name = "OrchestrationPipeline",
            string? description = null)
        {
            var pipeline = new StagePipeline<IStageContext>(name, description ?? "通用编排管线");
            pipeline.RegisterRange(GetDefaultStages());
            return pipeline;
        }

        public static StagePipeline<IStageContext> CreateCustomPipeline(
            Func<IEnumerable<IPipelineStage<IStageContext>>, IEnumerable<IPipelineStage<IStageContext>>> customize,
            string name = "CustomOrchestrationPipeline",
            string? description = null)
        {
            var stages = customize(GetDefaultStages());
            var pipeline = new StagePipeline<IStageContext>(name, description ?? "自定义编排管线");
            pipeline.RegisterRange(stages);
            return pipeline;
        }
    }
}

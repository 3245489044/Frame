
#nullable enable
using TConfig;
using Share.TaskStatusPool.Abstractions;
using Share.TaskStatusPool.Core;
using Share.TaskStatusPool.Morphisms;
using TConfig.Controls;

namespace Share.TaskStatusPool.ControlPool
{
    public static class ControlMorphismPipelineFactory
    {
        public static IMorphismPipeline<IPoolContext> CreateDefaultPipeline(
            ControlPool controlPool,
            ControlParameterRegistry registry,
            IEventExecutionService executionService,
            IControlValueAccessor valueAccessor)
        {
            PortGuard.Required(controlPool, nameof(controlPool));
            PortGuard.Required(registry, nameof(registry));
            PortGuard.Required(executionService, nameof(executionService));
            PortGuard.Required(valueAccessor, nameof(valueAccessor));

            var pipeline = new MorphismPipeline<IPoolContext>("ControlPipeline",
                "控件读写变换管线：读控件→映射参数→执行函数→写回结果→跨模块同步");

            pipeline
                .Register(new ControlReadMorphism(controlPool))
                .Register(new ParameterMapMorphism(registry))
                .Register(new FunctionExecuteMorphism(executionService))
                .Register(new ResultWriteMorphism(controlPool, valueAccessor))
                .Register(new CrossFormSyncMorphism(controlPool));

            return pipeline;
        }

        public static IMorphismPipeline<IPoolContext> CreateSimplePipeline(
            ControlPool controlPool,
            ControlParameterRegistry registry,
            IEventExecutionService executionService,
            IControlValueAccessor valueAccessor)
        {
            PortGuard.Required(controlPool, nameof(controlPool));
            PortGuard.Required(registry, nameof(registry));
            PortGuard.Required(executionService, nameof(executionService));
            PortGuard.Required(valueAccessor, nameof(valueAccessor));

            var pipeline = new MorphismPipeline<IPoolContext>("ControlPipeline-Simple",
                "简化控件读写变换管线：读控件→映射参数→执行函数→写回结果");

            pipeline
                .Register(new ControlReadMorphism(controlPool))
                .Register(new ParameterMapMorphism(registry))
                .Register(new FunctionExecuteMorphism(executionService))
                .Register(new ResultWriteMorphism(controlPool, valueAccessor));

            return pipeline;
        }
    }
}

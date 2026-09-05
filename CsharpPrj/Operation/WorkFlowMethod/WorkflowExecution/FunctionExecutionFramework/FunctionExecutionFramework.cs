
using BmFunctionRegistry = TConfig.ITrConfigRegistry;
using BmFunctionExecutor = ExecutionFramework.Engine.FunctionExecutor;
using BmPipelineExecutor = ExecutionFramework.Engine.PipelineExecutor;
using BmPipelineBuilder = ExecutionFramework.Engine.PipelineBuilder;
using BmIFunctionUnit = TConfig.IFunctionUnit;
using BmIPipelineBuilder = TConfig.IPipelineBuilder;

using BmPipelineDefinition = TConfig.TrPipeline;
using BmExecutionResult = TConfig.ExecutionResult;
using BmExecutionContext = TConfig.ExecutionContext;
using WorkFlowMethod.FunctionExecutionFramework.Core;
using WorkFlowMethod.FunctionExecutionFramework.ExecutionPipeline;
using WorkFlowMethod.Orchestration.Context.Models;
namespace WorkFlowMethod.FunctionExecutionFramework
{
    public static class FunctionExecutionFramework
    {
        private static readonly Lazy<DefaultWorkflowRegistry> _defaultRegistry = new(() =>
        {
            return new DefaultWorkflowRegistry();
        });

        private static readonly Lazy<BmPipelineExecutor> _defaultPipelineExecutor = new(() => new BmPipelineExecutor(_defaultRegistry.Value));

        public static DefaultWorkflowRegistry Registry => _defaultRegistry.Value;

        public static BmPipelineExecutor Pipeline => _defaultPipelineExecutor.Value;


        public static Task<BmExecutionResult> ExecuteAsync(string functionName, object? parameters = null, BmExecutionContext? context = null)
            => new BmFunctionExecutor(
                _defaultRegistry.Value,
                new ExecutionFramework.Abstractions.NullParameterProcessor(),
                new ExecutionFramework.Engine.FallbackTypeConverter())
            .ExecuteAsync(functionName, parameters is object[] arr ? arr : new[] { parameters }, context);

        public static Task<BmExecutionResult> ExecuteAsync(Delegate function, object? parameters = null, BmExecutionContext? context = null)
            => _defaultPipelineExecutor.Value.ExecutePipelineAsync(
                new BmPipelineBuilder().AddStep("execute", function).Build(), context);

        public static Task<BmExecutionResult> ExecuteAsync(BmIFunctionUnit unit, object? input = null, BmExecutionContext? context = null)
            => _defaultPipelineExecutor.Value.ExecutePipelineAsync(
                new BmPipelineBuilder().AddStep(unit.Name, unit).Build(), context);

        public static BmIPipelineBuilder CreatePipeline(string? name = null)
            => new BmPipelineBuilder();

        public static Task<BmExecutionResult> ExecutePipelineAsync(BmPipelineDefinition pipeline, BmExecutionContext? context = null)
            => _defaultPipelineExecutor.Value.ExecutePipelineAsync(pipeline, context);

    }
}


using BmPipelineDefinition = TConfig.TrPipeline;
using BmExecutionContext = TConfig.ExecutionContext;
using BmExecutionResult = TConfig.ExecutionResult;
using BmIPipelineBuilder = TConfig.IPipelineBuilder;

using TConfig;

namespace WorkFlowMethod.FunctionExecutionFramework.ExecutionPipeline
{
    public class ExecutionPipelineBuilder : IExecutionPipeline
    {
        private readonly ExecutionPipelineRunner _inner;

        public ExecutionPipelineBuilder(ITrConfigRegistry? registry = null)
        {
            _inner = new ExecutionPipelineRunner(registry);
        }

        public BmIPipelineBuilder CreateBuilder(string? name = null)
            => _inner.CreateBuilder(name);

        public Task<BmExecutionResult> ExecutePipelineAsync(
            BmPipelineDefinition pipeline, BmExecutionContext? context = null)
            => _inner.ExecutePipelineAsync(pipeline, context);

        public Task<BmExecutionResult> ExecuteStepAsync<TDelegate>(
            string stepName, TDelegate function, object? input = null)
            where TDelegate : Delegate
            => _inner.ExecuteStepAsync(stepName, function, input);

        public Task<BmExecutionResult> ExecuteStepsAsync<TDelegate>(
            params (string Name, TDelegate Function)[] steps)
            where TDelegate : Delegate
            => _inner.ExecuteStepsAsync(steps);

        public BmPipelineDefinition? LoadPipelineFromFile(string filePath)
            => _inner.LoadPipelineFromFile(filePath);

        public BmPipelineDefinition? LoadPipelineFromJson(string json)
            => _inner.LoadPipelineFromJson(json);

        public Task<BmExecutionResult> ExecuteFromFileAsync(
            string filePath, BmExecutionContext? context = null)
            => _inner.ExecuteFromFileAsync(filePath, context);

        public Task<BmExecutionResult> ExecuteFromJsonAsync(
            string json, BmExecutionContext? context = null)
            => _inner.ExecuteFromJsonAsync(json, context);

        public void SavePipelineToFile(BmPipelineDefinition pipeline, string filePath)
            => _inner.SavePipelineToFile(pipeline, filePath);
    }
}
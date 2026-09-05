
using BmIPipelineBuilder = TConfig.IPipelineBuilder;

using BmPipelineDefinition = TConfig.TrPipeline;
using BmExecutionResult = TConfig.ExecutionResult;
using BmExecutionContext = TConfig.ExecutionContext;

namespace WorkFlowMethod.FunctionExecutionFramework.ExecutionPipeline
{
    public interface IExecutionPipeline
    {
        BmIPipelineBuilder CreateBuilder(string? name = null);

        Task<BmExecutionResult> ExecutePipelineAsync(
            BmPipelineDefinition pipeline, BmExecutionContext? context = null);

        Task<BmExecutionResult> ExecuteStepAsync<TDelegate>(
            string stepName, TDelegate function, object? input = null)
            where TDelegate : Delegate;

        Task<BmExecutionResult> ExecuteStepsAsync<TDelegate>(
            params (string Name, TDelegate Function)[] steps)
            where TDelegate : Delegate;

        BmPipelineDefinition? LoadPipelineFromFile(string filePath);

        BmPipelineDefinition? LoadPipelineFromJson(string json);

        Task<BmExecutionResult> ExecuteFromFileAsync(
            string filePath, BmExecutionContext? context = null);

        Task<BmExecutionResult> ExecuteFromJsonAsync(
            string json, BmExecutionContext? context = null);

        void SavePipelineToFile(BmPipelineDefinition pipeline, string filePath);
    }
}

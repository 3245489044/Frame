
using BmPipelineDefinition = TConfig.TrPipeline;
using BmExecutionContext = TConfig.ExecutionContext;
using BmExecutionResult = TConfig.ExecutionResult;
using BmIPipelineBuilder = TConfig.IPipelineBuilder;

using BmPipelineExecutor = ExecutionFramework.Engine.PipelineExecutor;
using BmPipelineBuilder = ExecutionFramework.Engine.PipelineBuilder;
using TConfig;

namespace WorkFlowMethod.FunctionExecutionFramework.ExecutionPipeline
{
    public class ExecutionPipelineRunner : IExecutionPipeline
    {
        private readonly BmPipelineExecutor _executor;

        public ExecutionPipelineRunner(ITrConfigRegistry? registry = null)
        {
            _executor = new BmPipelineExecutor(registry);
        }

        public BmIPipelineBuilder CreateBuilder(string? name = null)
            => _executor.CreateBuilder(name);

        public Task<BmExecutionResult> ExecutePipelineAsync(
            BmPipelineDefinition pipeline, BmExecutionContext? context = null)
            => _executor.ExecutePipelineAsync(pipeline, context);

        public async Task<BmExecutionResult> ExecuteStepAsync<TDelegate>(
            string stepName, TDelegate function, object? input = null)
            where TDelegate : Delegate
        {
            var pipeline = new BmPipelineBuilder()
                .AddStep(stepName, function)
                .Build();

            return await _executor.ExecutePipelineAsync(pipeline, input as BmExecutionContext);
        }

        public async Task<BmExecutionResult> ExecuteStepsAsync<TDelegate>(
            params (string Name, TDelegate Function)[] steps)
            where TDelegate : Delegate
        {
            var builder = new BmPipelineBuilder();
            foreach (var (name, function) in steps)
            {
                builder.AddStep(name, function);
            }
            return await _executor.ExecutePipelineAsync(builder.Build());
        }

        public BmPipelineDefinition? LoadPipelineFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            return LoadPipelineFromJson(json);
        }

        public BmPipelineDefinition? LoadPipelineFromJson(string json)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<BmPipelineDefinition>(
                    json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        public async Task<BmExecutionResult> ExecuteFromFileAsync(
            string filePath, BmExecutionContext? context = null)
        {
            var pipeline = LoadPipelineFromFile(filePath);
            if (pipeline == null)
                return BmExecutionResult.Failure($"无法加载管线文件: {filePath}");

            return await _executor.ExecutePipelineAsync(pipeline, context);
        }

        public async Task<BmExecutionResult> ExecuteFromJsonAsync(
            string json, BmExecutionContext? context = null)
        {
            var pipeline = LoadPipelineFromJson(json);
            if (pipeline == null)
                return BmExecutionResult.Failure("无法解析 JSON 管线定义");

            return await _executor.ExecutePipelineAsync(pipeline, context);
        }

        public void SavePipelineToFile(BmPipelineDefinition pipeline, string filePath)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(pipeline,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}

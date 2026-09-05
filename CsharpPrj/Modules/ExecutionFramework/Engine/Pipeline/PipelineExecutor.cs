using ExecutionFramework.Abstractions;
using Modules.Transformers;
using TConfig;
using EXContext = TConfig.ExecutionContext;


namespace ExecutionFramework.Engine;

public class PipelineExecutor : TConfig.IPipelineExecutor

{
    private readonly ITrConfigRegistry _registry;
    private readonly TConfig.IFunctionExecutor _executor;


    public PipelineExecutor(ITrConfigRegistry? registry = null, TConfig.IFunctionExecutor? executor = null)

    {
        _registry = registry ?? new TrConfigRegistry();
        _executor = executor ?? new FunctionExecutor(_registry,
            new NullParameterProcessor(),
            new FallbackTypeConverter());
    }

    public async Task<ExecutionResult> ExecutePipelineAsync(
        TrPipeline pipeline,
        EXContext? context = null)
    {
        context ??= new EXContext();
        object? currentInput = null;

        foreach (var step in pipeline.Steps)
        {
            var result = await ExecuteStepAsync(step, currentInput, context);
            if (!result.IsSuccess)
                return result;
            currentInput = result.Value;
        }

        if (pipeline.FlattenSingleResult && currentInput is List<object> list && list.Count == 1)
            currentInput = list[0];

        return ExecutionResult.Success(currentInput);
    }

    public TConfig.IPipelineBuilder CreateBuilder(string? name = null)

    {
        return new PipelineBuilder(_registry) { Name = name ?? "Pipeline" };
    }

    private async Task<ExecutionResult> ExecuteStepAsync(
        TrStep step,
        object? input,
        EXContext context)
    {
        var stepExecutor = BuildStepExecutor(step, context);
        return await stepExecutor(input);
    }

    private Func<object?, Task<ExecutionResult>> BuildStepExecutor(TrStep step, EXContext context)
    {
        if (step.IsParallel && step.ParallelSteps != null)
            return input => ExecuteParallelStepsAsync(step.ParallelSteps, context);

        if (step.SubPipeline != null)
            return _ => ExecutePipelineAsync(step.SubPipeline, context);

        if (step.Condition != null)
        {
            return async input =>
            {
                if (!step.Condition(input))
                    return ExecutionResult.Success(null);
                return await ExecuteCoreAsync(step, input, context);
            };
        }

        return input => ExecuteCoreAsync(step, input, context);
    }

    private async Task<ExecutionResult> ExecuteCoreAsync(TrStep step, object? input, EXContext context)
    {
        object? stepInput = step.UseExplicitParameters ? step.Parameters : input;

        if (step.Transformer is ITransformer transformer)
        {
            var result = InvokeTransformer(transformer, stepInput);
            return ExecutionResult.Success(result);
        }


        if (step.Function != null)
        {
            return await _executor.ExecuteAsync(step.Function, stepInput, context);
        }

        return ExecutionResult.Success(stepInput);
    }

    private static object? InvokeTransformer(ITransformer transformer, object? input)
    {
        if (transformer is ITransformer<object?, object?> typedTransformer)
            return typedTransformer.Transform(input);

        var method = transformer.GetType().GetMethod("Transform", [typeof(object)]);
        if (method != null)
            return method.Invoke(transformer, [input]);

        throw new InvalidOperationException(
            $"Transformer '{transformer.GetType().Name}' does not have a compatible Transform method.");
    }


    private async Task<ExecutionResult> ExecuteParallelStepsAsync(
        List<TrStep> steps,
        EXContext context)
    {
        var tasks = steps.Select(s => ExecuteStepAsync(s, null, context));
        var results = await Task.WhenAll(tasks);

        var failed = results.FirstOrDefault(r => !r.IsSuccess);
        if (failed != null)
            return failed;

        var values = results.Select(r => r.Value).ToList();
        return ExecutionResult.Success(values);
    }
}

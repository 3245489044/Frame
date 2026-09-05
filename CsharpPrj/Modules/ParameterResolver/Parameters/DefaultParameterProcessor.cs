using ParameterResolver.Parameters.Abstractions;
using TConfig;
using EXContext = TConfig.ExecutionContext;

namespace ParameterResolver.Parameters;

public class DefaultParameterProcessor : IParameterProcessor
{
    public static Func<ITrConfigRegistry, object>? FunctionExecutorFactory { get; set; }

    public async Task<object> ProcessAsync(object input, ITrConfigRegistry registry, EXContext? context = null)
    {
        if (input is ITrCall call)
            return await ProcessTrCallAsync(call, registry, context);

        if (input is System.Collections.IEnumerable enumerable 
            && input is not string 
            && input is not System.Collections.IDictionary)
        {
            var list = enumerable.Cast<object?>().ToList();
            var processedTasks = list
                .Select(i => i is ITrCall c
                    ? ProcessTrCallAsync(c, registry, context)
                    : Task.FromResult<object>(i!));
            var processed = await Task.WhenAll(processedTasks);
            return processed.ToList();
        }

        return input;
    }

    public async Task<IEnumerable<object>> ProcessManyAsync(
        IEnumerable<object> inputs, ITrConfigRegistry registry, EXContext? context = null)
    {
        if (inputs == null)
            return Enumerable.Empty<object>();

        var tasks = inputs.Select(i => ProcessAsync(i, registry, context));
        return await Task.WhenAll(tasks);
    }

    private async Task<object> ProcessTrCallAsync(
        ITrCall call, ITrConfigRegistry registry, EXContext? context)
    {
        if (call.NestedCall != null)
        {
            var nestedResult = await ProcessTrCallAsync(call.NestedCall, registry, context);
            var newParams = new List<object?>(call.Parameters);
            int insertIndex = call.NestedCallParameterIndex >= 0 && call.NestedCallParameterIndex < newParams.Count
                ? call.NestedCallParameterIndex
                : 0;
            newParams[insertIndex] = nestedResult;
            return await ExecuteFunctionAsync(call.ConfigName, newParams, registry, context);
        }
        return await ExecuteFunctionAsync(call.ConfigName, call.Parameters, registry, context);
    }

    private async Task<object> ExecuteFunctionAsync(
        string name, List<object?> parameters, ITrConfigRegistry registry, EXContext? context)
    {
        if (!registry.TryGetConfig(name, out var config))
            throw new InvalidOperationException($"Function '{name}' not found in registry");

        if (FunctionExecutorFactory == null)
            throw new InvalidOperationException(
                "DefaultParameterProcessor.FunctionExecutorFactory is not set. " +
                "Call ExecutionFramework bootstrapper to initialize it.");

        dynamic executor = FunctionExecutorFactory(registry);
        dynamic result = await executor.ExecuteAsync(config.Name, parameters.ToArray(), context);

        return result.Value ?? new object();
    }
}

﻿using System.Diagnostics;
using ExecutionFramework.Abstractions;
using TConfig;

namespace ExecutionFramework.Engine;

public class StepExecutor : IStepExecutor
{
    private readonly IExecutionLogger? _logger;
    private readonly TConfig.IFunctionExecutor? _functionExecutor;

    public StepExecutor(
        TConfig.IFunctionExecutor? functionExecutor = null,
        IExecutionLogger? logger = null)
    {
        _functionExecutor = functionExecutor;
        _logger = logger;
    }

    public async Task<TConfig.ExecutionResult> ExecuteStepAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        _logger?.LogStepStart(step.Id, step.Name);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = step.StepType switch
            {
                "Function" => await ExecuteFunctionStepAsync(step, context, cancellationToken),
                "Condition" => await ExecuteConditionStepAsync(step, context, cancellationToken),
                "Loop" => await ExecuteLoopStepAsync(step, context, cancellationToken),
                "Parallel" => await ExecuteParallelStepAsync(step, context, cancellationToken),
                _ => await ExecuteFunctionStepAsync(step, context, cancellationToken)
            };

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                _logger?.LogStepComplete(step.Id, step.Name, stopwatch.Elapsed);
            }
            else
            {
                _logger?.LogStepError(step.Id, step.Name, result.ErrorMessage ?? "Unknown error");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger?.LogStepError(step.Id, step.Name, "Step execution was cancelled");
            return TConfig.ExecutionResult.Cancelled();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogStepError(step.Id, step.Name, ex.Message);
            return TConfig.ExecutionResult.Failure($"Step '{step.Name}' failed: {ex.Message}", ex);
        }
    }

    public bool CanExecute(StepDefinition step, TConfig.IExecutionContext context)
    {
        return step.IsEnabled && !step.IsSkipped;
    }

    protected virtual async Task<TConfig.ExecutionResult> ExecuteFunctionStepAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug($"Executing function step '{step.Name}' with function '{step.FunctionName}'");

        if (_functionExecutor == null || string.IsNullOrEmpty(step.FunctionName))
            return TConfig.ExecutionResult.Success(null);

        using var timeoutCts = step.TimeoutMs > 0 ? new CancellationTokenSource() : null;

        if (timeoutCts != null)
            timeoutCts.CancelAfter(step.TimeoutMs);

        try
        {
            var namedParameters = GetNamedParameters(step);
            if (namedParameters != null)
            {
                var namedResult = await _functionExecutor.ExecuteAsync(step.FunctionName, namedParameters, context);
                return namedResult;
            }

            var paramsArray = GetStepParameters(step);
            var result = await _functionExecutor.ExecuteAsync(step.FunctionName, paramsArray, context);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            _logger?.LogError($"Step '{step.Name}' timed out after {step.TimeoutMs}ms");
            return TConfig.ExecutionResult.Failure($"Step '{step.Name}' timed out after {step.TimeoutMs}ms");
        }
    }
    private static Dictionary<string, object>? GetNamedParameters(StepDefinition step)
    {
        if (step.Parameters is null)
            return null;

        return step.Parameters switch
        {
            Dictionary<string, object?> dict => dict.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value!),
            System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.Object =>
                json.EnumerateObject().ToDictionary(
                    prop => prop.Name,
                    prop => ConvertJsonElementToObject(prop.Value)),
            _ => null
        };
    }

    private static object? ConvertJsonElementToObject(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
            System.Text.Json.JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
            System.Text.Json.JsonValueKind.Number => element.GetDouble(),
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElementToObject).ToArray(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(prop => prop.Name, prop => ConvertJsonElementToObject(prop.Value)),
            _ => element.Clone()
        };
    }

    protected virtual async Task<TConfig.ExecutionResult> ExecuteConditionStepAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug($"Executing condition step '{step.Name}'");

        if (step.Children.Count == 0)
        {
            _logger?.LogWarning($"Condition step '{step.Name}' has no children");
            return TConfig.ExecutionResult.Success(null);
        }

        bool conditionResult = await EvaluateConditionAsync(step, context, cancellationToken);

        int trueBranchCount = step.Metadata.TryGetValue("TrueBranchCount", out var count)
            ? Convert.ToInt32(count)
            : step.Children.Count;

        var branchSteps = conditionResult
            ? step.Children.Take(trueBranchCount)
            : step.Children.Skip(trueBranchCount);

        var results = new List<TConfig.ExecutionResult>();
        foreach (var child in branchSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteFunctionStepAsync(child, context, cancellationToken);
            results.Add(result);

            if (!result.IsSuccess)
                return result;
        }

        return TConfig.ExecutionResult.Success(results);
    }

    private async Task<bool> EvaluateConditionAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.FunctionName))
        {
            _logger?.LogDebug($"Condition step '{step.Name}' has no condition function, defaulting to true");
            return true;
        }

        try
        {
            var paramsArray = GetStepParameters(step);
            var result = await _functionExecutor!.ExecuteAsync(step.FunctionName, paramsArray, context);
            if (result.IsSuccess && result.Value is bool boolVal)
                return boolVal;
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Condition function '{step.FunctionName}' failed: {ex.Message}");
            return false;
        }
    }

    protected virtual async Task<TConfig.ExecutionResult> ExecuteLoopStepAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug($"Executing loop step '{step.Name}'");

        if (step.Children.Count == 0)
        {
            _logger?.LogWarning($"Loop step '{step.Name}' has no body steps");
            return TConfig.ExecutionResult.Success(null);
        }

        int maxIterations = step.Metadata.TryGetValue("MaxIterations", out var maxVal)
            ? Convert.ToInt32(maxVal)
            : Math.Max(1, step.RetryCount + 1);

        object? lastResult = null;
        for (int i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger?.LogDebug($"Loop step '{step.Name}' iteration {i + 1}/{maxIterations}");

            var iterationContext = CreateIterationContext(context, lastResult);

            foreach (var child in step.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ExecuteFunctionStepAsync(child, iterationContext, cancellationToken);

                if (!result.IsSuccess)
                    return result;

                lastResult = result.Value;
            }
        }

        return TConfig.ExecutionResult.Success(lastResult);
    }

    private static TConfig.IExecutionContext CreateIterationContext(TConfig.IExecutionContext context, object? previousResult)
    {
        if (previousResult == null)
            return context;

        var subContext = context.CreateSubContext(context.NodeKey);
        subContext.SetData("PreviousResult", previousResult);
        return subContext;
    }

    protected virtual async Task<TConfig.ExecutionResult> ExecuteParallelStepAsync(
        StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug($"Executing parallel step '{step.Name}' with {step.Children.Count} children");

        if (step.Children.Count == 0)
        {
            _logger?.LogWarning($"Parallel step '{step.Name}' has no children");
            return TConfig.ExecutionResult.Success(null);
        }

        var tasks = step.Children
            .Select(child => ExecuteFunctionStepAsync(child, context, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var failed = results.FirstOrDefault(r => !r.IsSuccess);
        if (failed != null)
        {
            _logger?.LogError($"Parallel step '{step.Name}' failed: {failed.ErrorMessage}");
            return failed;
        }

        var values = results.Select(r => r.Value).ToList();
        return TConfig.ExecutionResult.Success(values);
    }

    private static object?[]? GetStepParameters(StepDefinition step)
    {
        if (step.Parameters is null)
            return null;
        return step.Parameters is object?[] arr ? arr : new[] { step.Parameters };
    }
}

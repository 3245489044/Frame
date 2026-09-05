
using System.Diagnostics;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Services;
using TConfig;
using IEventTriggerService = TConfig.IEventTriggerService;
using IEventTriggerResult = TConfig.IEventTriggerResult;
using ExecutorIFunctionExecutor = TConfig.IFunctionExecutor;

namespace Share.EventBindingOrchestrator.Services;

public class EventTriggerService : IEventTriggerService,
    Share.EventBindingOrchestrator.Abstractions.IEventTriggerService
{
    private readonly IEventBindingResolver _resolver;
    private readonly ExecutorIFunctionExecutor _executor;
    private readonly IParameterBuilder<string>? _parameterBuilder;
    private readonly IValueWriter? _valueWriter;

    public EventTriggerService(
        IEventBindingResolver resolver,
        ExecutorIFunctionExecutor executor,
        IParameterBuilder<string>? parameterBuilder = null,
        IValueWriter? valueWriter = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _parameterBuilder = parameterBuilder;
        _valueWriter = valueWriter;
    }


    public async Task<IEventTriggerResult> TriggerEventAsync(
        IEventSourceNode source, string eventName, object? eventArgs)
    {
        return await ExecuteCoreAsync(
            () => _resolver.FindEventBinding(((IEventSource)source).Name, source.SourceType),
            binding =>
            {
                if (_parameterBuilder != null)
                {
                    var p = _parameterBuilder.Build(source, Array.Empty<string>(),
                        (s, n) => ResolveInputValue(source, n));
                    if (p.Count == 0 && eventArgs != null) p["value"] = eventArgs;
                    return p;
                }
                var fallback = new Dictionary<string, object?>();
                if (eventArgs != null) fallback["value"] = eventArgs;
                return fallback;
            },
            (result, binding, elapsed) =>
            {
                if (!string.IsNullOrEmpty(binding.Output) && result.Value != null && _valueWriter != null)
                    _ = _valueWriter.WriteAsync(source, binding.Output, result.Value);
                return Models.EventTriggerResult.SuccessResult(
                    result.Value, eventName: eventName,
                    functionName: binding.FunctionName,
                    outputTarget: binding.Output,
                    executionTime: elapsed);
            },
            (errMsg, elapsed) => Models.EventTriggerResult.FailureResult(
                errMsg, eventName: eventName, executionTime: elapsed),
            eventName);
    }


    public async Task<IEventTriggerResult> TriggerEventAsync(
        string eventName, object? sender, object? args)
    {
        return await ExecuteCoreAsync(
            () => _resolver.FindEventBinding(eventName),
            binding => new Dictionary<string, object?>
            {
                ["value"] = args,
                ["sender"] = sender
            },
            (result, binding, elapsed) =>
            {
                if (!string.IsNullOrEmpty(binding.Output) && result.Value != null && _valueWriter != null)
                {
                    var sourceNode = sender as IEventSourceNode;
                    _ = _valueWriter.WriteAsync(sourceNode!, binding.Output, result.Value);
                }
                return Models.EventTriggerResult.SuccessResult(
                    result.Value, eventName: eventName,
                    functionName: binding.FunctionName,
                    outputTarget: binding.Output);
            },
            (errMsg, elapsed) => Models.EventTriggerResult.FailureResult(
                errMsg, eventName: eventName),
            eventName);
    }

    public async Task<IEventTriggerResult> TriggerAndExecuteAsync(
        string eventName, object? sender, object? args)
    {
        return await TriggerEventAsync(eventName, sender, args);
    }


    async Task<Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult>
        Share.EventBindingOrchestrator.Abstractions.IEventTriggerService.TriggerEventAsync(
            string controlName, string eventType, string value)
    {
        var result = (IEventTriggerResult)await ExecuteCoreAsync(
            () => _resolver.FindEventBinding(controlName, eventType),
            binding =>
            {
                var p = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(value)) p["value"] = value;
                return p;
            },
            (functionResult, binding, elapsed) =>
            {
                if (!string.IsNullOrEmpty(binding.Output) && _valueWriter != null)
                    _ = _valueWriter.WriteAsync(null!, binding.Output, functionResult.Value);
                return Models.EventTriggerResult.SuccessResult(
                    functionResult.Value, controlName, eventType,
                    binding.FunctionName, binding.Output, elapsed);
            },
            (errMsg, elapsed) => Models.EventTriggerResult.FailureResult(
                errMsg, controlName, eventType, executionTime: elapsed),
            eventType);

        return (Share.EventBindingOrchestrator.Abstractions.IEventTriggerResult)result;
    }


    private async Task<IEventTriggerResult> ExecuteCoreAsync(
        Func<IEventBindingInfo?> findBinding,
        Func<IEventBindingInfo, Dictionary<string, object?>> buildParams,
        Func<TConfig.FunctionExecutionResult, IEventBindingInfo, TimeSpan, IEventTriggerResult> mapSuccess,
        Func<string?, TimeSpan, IEventTriggerResult> mapFailure,
        string eventName)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var binding = findBinding();
            if (binding == null)
            {
                sw.Stop();
                return mapFailure(
                    $"事件源 '{eventName}' 未找到事件绑定",
                    sw.Elapsed);
            }

            var parameters = buildParams(binding);

            if (binding.FunctionNames.Count > 1)
                return await ExecuteMultiFunctionBindingAsync(
                    eventName, binding, parameters, sw, mapSuccess, mapFailure);

            var result = await _executor.ExecuteFunctionAsync(binding.FunctionName, parameters);
            if (!result.IsSuccess)
            {
                sw.Stop();
                return mapFailure(
                    $"函数 '{binding.FunctionName}' 执行失败: {result.ErrorMessage}",
                    sw.Elapsed);
            }

            sw.Stop();
            var successResult = mapSuccess(result, binding, sw.Elapsed);

            ExportDiagnosticSnapshot(eventName, binding.FunctionName);
            return successResult;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return mapFailure($"事件触发异常: {ex.Message}", sw.Elapsed);
        }
    }


    private async Task<IEventTriggerResult> ExecuteMultiFunctionBindingAsync(
        string eventName,
        IEventBindingInfo binding,
        Dictionary<string, object?> inputValues,
        Stopwatch sw,
        Func<TConfig.FunctionExecutionResult, IEventBindingInfo, TimeSpan, IEventTriggerResult> mapSuccess,
        Func<string?, TimeSpan, IEventTriggerResult> mapFailure)
    {
        IEventTriggerResult? lastResult = null;
        var errorMessages = new List<string>();

        foreach (var functionName in binding.FunctionNames)
        {
            var execResult = await _executor.ExecuteFunctionAsync(functionName, inputValues);

            if (!execResult.IsSuccess)
                errorMessages.Add($"{functionName}: {execResult.ErrorMessage}");

            if (execResult.IsSuccess)
            {
                lastResult = mapSuccess(execResult, binding, execResult.ExecutionTime);
                if (!string.IsNullOrEmpty(binding.Output) && _valueWriter != null)
                    await _valueWriter.WriteAsync(null!, binding.Output, execResult.Value);
            }
        }

        sw.Stop();

        if (lastResult != null)
            return lastResult;

        return mapFailure(
            errorMessages.Count > 0
                ? string.Join("; ", errorMessages)
                : "All functions returned no result",
            sw.Elapsed);
    }


    private static object? ResolveInputValue(IEventSourceNode source, string sourceName)
    {
        if (string.Equals(sourceName, ((IEventSource)source).Name, StringComparison.OrdinalIgnoreCase))
            return source.GetValue();

        var child = source.FindChild(sourceName);
        return child?.GetValue();
    }

    private static void ExportDiagnosticSnapshot(string eventName, string? functionName)
    {
        var currentScope = Modules.DiagnosticContext.Core.DiagnosticScopeStore.GetCurrent();
        if (currentScope == null) return;

        var snapshot = currentScope.ToJson();
        var truncated = snapshot.Length > 500 ? snapshot.Substring(0, 500) + "..." : snapshot;
        System.Diagnostics.Debug.WriteLine(
            $"[DiagnosticSnapshot] eventName={eventName}, " +
            $"functionName={functionName}, " +
            $"snapshot={Environment.NewLine}{truncated}");
    }
}
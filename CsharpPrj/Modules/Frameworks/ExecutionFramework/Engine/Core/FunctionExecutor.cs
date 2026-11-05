using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ExecutionFramework.Abstractions;
using TConfig;

namespace ExecutionFramework.Engine;

public class FunctionExecutor : TConfig.IFunctionExecutor
{
    /// <summary>诊断探针钩子（宿主注入日志器）；用于定位函数委托是否真正启动/完成。缺省 no-op。</summary>
    public static Action<string, object?>? Trace { get; set; }

    private readonly ITrConfigRegistry _registry;
    private readonly IParameterProcessor _parameterProcessor;
    private readonly ITypeConverter _typeConverter;
    private readonly TimeSpan _defaultTimeout;

    public FunctionExecutor(
        ITrConfigRegistry registry,
        IParameterProcessor parameterProcessor,
        ITypeConverter typeConverter,
        TimeSpan? defaultTimeout = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _parameterProcessor = parameterProcessor ?? throw new ArgumentNullException(nameof(parameterProcessor));
        _typeConverter = typeConverter ?? throw new ArgumentNullException(nameof(typeConverter));
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }


    public async Task<ExecutionResult> ExecuteAsync(string functionName, object?[]? parameters, IExecutionContext? context = null)
    {
        if (!_registry.TryGetConfig(functionName, out var config))
            return ExecutionResult.Failure($"Function '{functionName}' not found in registry");

        var execContext = EnsureExecutionContext(context);

        return await ExecuteAsync(config, parameters, execContext);
    }

    public async Task<ExecutionResult> ExecuteAsync(string functionName, Dictionary<string, object> namedParameters, IExecutionContext? context = null)
    {
        if (!_registry.TryGetConfig(functionName, out var config))
            return ExecutionResult.Failure($"Function '{functionName}' not found in registry");

        var execContext = EnsureExecutionContext(context);
        return await ExecuteWithTimeoutAsync(
            async (args, ct) =>
            {
                var args2 = ParameterTransformer.ForConfig(config, _typeConverter).Transform(args);
                return await InvokeDelegateAsync(config, args2, ct);
            },
            namedParameters, execContext);
    }

    public async Task<ExecutionResult> ExecuteAsync(Delegate function, object? parameters, IExecutionContext? context = null)
    {
        if (function == null) return ExecutionResult.Failure("Function delegate is null");

        var execContext = EnsureExecutionContext(context);
        return await ExecuteWithTimeoutAsync(
            async (args, ct) =>
            {
                var method = function.Method;
                var args2 = ParameterTransformer.ForMethod(method, _typeConverter).Transform(args);
                return await InvokeDelegateAsync(function, args2, ct);
            },
            parameters, execContext);
    }

    public async Task<ExecutionResult> ExecuteAsync(TConfig.IFunctionUnit unit, object? input, IExecutionContext? context = null)
    {
        if (unit == null) return ExecutionResult.Failure("Function unit is null");
        var execContext = EnsureExecutionContext(context);
        return await unit.ExecuteAsync(input, execContext);
    }

    public async Task<ExecutionResult> ExecuteAsync(ITrConfig config, object?[]? parameters, IExecutionContext? context = null)
    {
        if (config == null) return ExecutionResult.Failure("TrConfig is null");

        var execContext = EnsureExecutionContext(context);
        return await ExecuteWithTimeoutAsync(
            async (args, ct) =>
            {
                var args2 = ParameterTransformer.ForConfig(config, _typeConverter).Transform(args);
                return await InvokeDelegateAsync(config, args2, ct);
            },
            parameters, execContext);
    }

    public async Task<ExecutionResult<TResult>> ExecuteAsync<TResult>(string functionName, object?[]? parameters, IExecutionContext? context = null)
    {
        var result = await ExecuteAsync(functionName, parameters, context);
        return ExecutionResult<TResult>.From(result);
    }



    public async Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName, Dictionary<string, object?> parameters)
    {
        var dictParams = parameters?.ToNonNullDictionary()
            ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var result = await ExecuteAsync(functionName, dictParams);
        return new FunctionExecutionResult
        {
            IsSuccess = result.IsSuccess,
            Value = result.Value,
            ErrorMessage = result.ErrorMessage,
            ExecutionTime = result.ExecutionTime
        };
    }

    public bool HasFunction(string functionName)
    {
        return _registry.TryGetConfig(functionName, out _);
    }

    public IEnumerable<string> GetFunctionNames()
    {
        return _registry.GetRegisteredNames();
    }


    private static TConfig.ExecutionContext EnsureExecutionContext(IExecutionContext? context)
    {
        if (context is TConfig.ExecutionContext execContext)
            return execContext;

        var result = new TConfig.ExecutionContext();

        if (context != null)
        {
            result.CancellationToken = context.CancellationToken;
            result.ServiceProvider = context.ServiceProvider;
            result.WorkflowId = context.WorkflowId;
            result.NodeKey = context.NodeKey;
            foreach (var kvp in context.Items)
                result.Items[kvp.Key] = kvp.Value;
        }
        return result;
    }

    private async Task<ExecutionResult> ExecuteWithTimeoutAsync(
        Func<object?, CancellationToken, Task<object?>> executeFunc,
        object? parameters,
        TConfig.ExecutionContext context)

    {
        var stopwatch = Stopwatch.StartNew();
        CancellationTokenSource? cts = null;
        try
        {
            var processedParams = parameters != null
                ? await _parameterProcessor.ProcessAsync(parameters, _registry, context)
                : null;

            cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            // 单一权威计时（P2）：框架已提供 ct（内核 AEF 预算已承载超时）→ 不再叠加内层 _defaultTimeout，
            // 避免 60s 预算的 plan 被内层 30s 误杀；仅对无外部预算的直接调用保留安全兜底。
            if (context.CancellationToken == CancellationToken.None)
                cts.CancelAfter(_defaultTimeout);

            var result = await executeFunc(processedParams, cts.Token);
            stopwatch.Stop();

            if (result is bool boolResult)
            {
                return boolResult
                    ? ExecutionResult.Succeed(result, stopwatch.Elapsed)
                    : ExecutionResult.Fail("注册函数返回 false（失败，详情经 FsLog 记录）");
            }

            return ExecutionResult.Succeed(result, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cts is { IsCancellationRequested: true }
            && !context.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ExecutionResult.Timeout();
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ExecutionResult.Cancelled();
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            return ExecutionResult.Timeout();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ExecutionResult.Failure(ex.Message, ex);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 通用委托执行入口（框架层统一 数组委托适配 / 异步解包 / 超时 / 取消 / 异常→ExecutionResult）。
    /// 区分两类委托：
    ///  ① 数组委托 —— 唯一参数为 object[]（如 AutoRegister passthrough 反射注册的 Func&lt;object[], Task&lt;object&gt;&gt;）：
    ///     把参数数组作为单个 object[] 传入（DynamicInvoke(new object[] { args })）。
    ///  ② 位置参数委托 —— 如 MathLibrary 的 Func&lt;double,double,double&gt;：逐位展开（DynamicInvoke(args)）。
    /// 消费方：FunctionTesterFunctions.Func_Execute 等直接持有委托的执行点，复用本框架完整异常处理，
    /// 避免各点自行 DynamicInvoke + 吞异常导致失败被静默。
    /// </summary>
    public async Task<ExecutionResult> ExecuteDelegateAsync(Delegate function, object?[]? args, CancellationToken cancellationToken = default)
    {
        if (function == null)
            return ExecutionResult.Failure("委托为空");

        var stopwatch = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Fix·单一时钟（R1/E3）：与 ExecuteWithTimeoutAsync 对齐——已有外部 ct（框架 AEF 预算承载超时）时不叠加内层 30s，
        // 避免长任务被内层默认超时误杀；仅对无外部预算的直接调用保留安全兜底。
        if (cancellationToken == CancellationToken.None)
            cts.CancelAfter(_defaultTimeout);

        try
        {
            var parms = function.Method.GetParameters();
            var isArrayDelegate = parms.Length == 1 && parms[0].ParameterType == typeof(object[]);

            var invokeArgs = isArrayDelegate
                ? new object?[] { args! }
                : CancellationTokenInjector.Inject(function, args!, cts.Token);

            var result = await TConfig.TaskHelper.InvokeAndUnwrapAsync(
                () => function.DynamicInvoke(invokeArgs),
                function.Method.ReturnType,
                cts.Token).ConfigureAwait(false);

            stopwatch.Stop();
            return ExecutionResult.Succeed(result, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ExecutionResult.Timeout();
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ExecutionResult.Cancelled();
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            Trace?.Invoke("FunctionExecutor_DelegateFailed",
                new { method = function.Method?.Name, error = "委托执行超时", elapsedMs = stopwatch.ElapsedMilliseconds });
            return ExecutionResult.Timeout();
        }
        catch (TargetInvocationException ex)
        {
            stopwatch.Stop();
            var inner = ex.InnerException ?? ex;
            Trace?.Invoke("FunctionExecutor_DelegateFailed",
                new { method = function.Method?.Name, error = inner.Message, errorType = inner.GetType().Name, elapsedMs = stopwatch.ElapsedMilliseconds });
            return ExecutionResult.Failure(inner.Message, inner);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Trace?.Invoke("FunctionExecutor_DelegateFailed",
                new { method = function.Method?.Name, error = ex.Message, errorType = ex.GetType().Name, elapsedMs = stopwatch.ElapsedMilliseconds });
            return ExecutionResult.Failure(ex.Message, ex);
        }
    }

    private static async Task<object?> InvokeDelegateAsync(Delegate function, object[] args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invokeArgs = CancellationTokenInjector.Inject(function, args, cancellationToken);
        return await TConfig.TaskHelper.InvokeAndUnwrapAsync(
            () => function.DynamicInvoke(invokeArgs),
            function.Method.ReturnType,
            cancellationToken);
    }

    private static async Task<object?> InvokeDelegateAsync(ITrConfig config, object[] args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 取消贯穿（P3）：FunctionDescriptor 携带底层 Delegate；若其声明了 CancellationToken 参数，
        // 则把调用方 ct 注入到正确位置，使框架取消能贯穿到真正执行子进程的函数（如 ClineClient）。
        // config.Parameters 已过滤 ct（见 FunctionDescriptor.FromDelegate），故这里在 args 上补位。
        var invokeArgs = config is TConfig.FunctionDescriptor fd && fd.Delegate != null
            ? CancellationTokenInjector.Inject(fd.Delegate, args, cancellationToken)
            : args;
        return await TConfig.TaskHelper.InvokeAndUnwrapAsync(
            () => config.Invoke(invokeArgs),
            config.ReturnType,
            cancellationToken);
    }
}

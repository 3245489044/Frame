
using System.Diagnostics;
using Container.Abstractions;
using Microsoft.Extensions.Logging;

namespace Container.Decorators;

public class LoggingDecorator : IEventFunctionExecutor
{
    private readonly IEventFunctionExecutor _inner;
    private readonly ILogger<LoggingDecorator> _logger;

    public LoggingDecorator(
        IEventFunctionExecutor inner,
        ILogger<LoggingDecorator> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation(
                "函数 {FunctionName} 开始执行，参数数: {ParamCount}",
                functionName, parameters?.Count ?? 0);

            var result = await _inner.ExecuteFunctionAsync(functionName, parameters, cancellationToken);

            sw.Stop();
            if (result.Success)
            {
                _logger.LogInformation(
                    "函数 {FunctionName} 执行成功，耗时 {Elapsed}ms",
                    functionName, sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "函数 {FunctionName} 执行失败，耗时 {Elapsed}ms，错误: {Error}",
                    functionName, sw.ElapsedMilliseconds, result.Error);
            }

            return result with { ExecutionTime = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "函数 {FunctionName} 执行异常，耗时 {Elapsed}ms",
                functionName, sw.ElapsedMilliseconds);
            return FunctionExecutionResult.Fail(ex.Message, sw.Elapsed);
        }
    }
}


using System.Diagnostics;
using Container.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Container.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
    where TResponse : ExecutionResult
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            _logger.LogInformation(
                "命令 {Command} 执行完成，耗时 {Elapsed}ms",
                request.CommandText, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "命令 {Command} 执行失败，耗时 {Elapsed}ms",
                request.CommandText, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

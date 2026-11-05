using Microsoft.Extensions.Logging;

namespace ExecutionFramework.Logging;

using Abstractions;

public class ExecutionLogger : IExecutionLogger
{
    private readonly ILogger _logger;

    public ExecutionLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void LogInformation(string message, object? data = null)
    {
        _logger.LogInformation("[Execution] {Message} {Data}", message, data);
    }

    public void LogWarning(string message, object? data = null)
    {
        _logger.LogWarning("[Execution] {Message} {Data}", message, data);
    }

    public void LogError(string message, Exception? exception = null, object? data = null)
    {
        _logger.LogError(exception, "[Execution] {Message} {Data}", message, data);
    }

    public void LogDebug(string message, object? data = null)
    {
        _logger.LogDebug("[Execution] {Message} {Data}", message, data);
    }

    public void LogExecutionStart(string planId, string? planName = null)
    {
        _logger.LogInformation("[Execution] Starting execution '{PlanName}' (ID: {PlanId})",
            planName ?? "Unknown", planId);
    }

    public void LogExecutionComplete(string planId, TimeSpan executionTime)
    {
        _logger.LogInformation("[Execution] Execution '{PlanId}' completed in {ExecutionTime}ms",
            planId, executionTime.TotalMilliseconds);
    }

    public void LogExecutionError(string planId, string errorMessage, Exception? exception = null)
    {
        _logger.LogError(exception, "[Execution] Execution '{PlanId}' failed: {ErrorMessage}",
            planId, errorMessage);
    }

    public void LogStepStart(string stepId, string stepName)
    {
        _logger.LogInformation("[Execution] Starting step '{StepName}' (ID: {StepId})",
            stepName, stepId);
    }

    public void LogStepComplete(string stepId, string stepName, TimeSpan executionTime)
    {
        _logger.LogInformation("[Execution] Step '{StepName}' completed in {ExecutionTime}ms",
            stepName, executionTime.TotalMilliseconds);
    }

    public void LogStepError(string stepId, string stepName, string errorMessage)
    {
        _logger.LogError("[Execution] Step '{StepName}' failed: {ErrorMessage}",
            stepName, errorMessage);
    }
}

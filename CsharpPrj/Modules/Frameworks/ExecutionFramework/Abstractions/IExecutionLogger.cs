namespace ExecutionFramework.Abstractions;

public interface IExecutionLogger
{
    void LogInformation(string message, object? data = null);

    void LogWarning(string message, object? data = null);

    void LogError(string message, Exception? exception = null, object? data = null);

    void LogDebug(string message, object? data = null);

    void LogExecutionStart(string planId, string? planName = null);

    void LogExecutionComplete(string planId, TimeSpan executionTime);

    void LogExecutionError(string planId, string errorMessage, Exception? exception = null);

    void LogStepStart(string stepId, string stepName);

    void LogStepComplete(string stepId, string stepName, TimeSpan executionTime);

    void LogStepError(string stepId, string stepName, string errorMessage);
}

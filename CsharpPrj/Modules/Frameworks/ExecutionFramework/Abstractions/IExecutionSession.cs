namespace ExecutionFramework.Abstractions;

using TConfig;

public interface IExecutionSession
{
    string SessionId { get; }

    string WorkflowId { get; }

    DateTime StartTime { get; }

    DateTime? EndTime { get; }

    ExecutionStatus Status { get; }

    IDictionary<string, object> Metadata { get; }

    void MarkRunning();

    void MarkCompleted();

    void MarkFailed(string? errorMessage = null, Exception? exception = null);

    void MarkCancelled();

    void MarkPaused();

    void MarkResumed();

    string? GetErrorMessage();

    Exception? GetException();

    TimeSpan GetExecutionTime();
}

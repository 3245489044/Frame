
namespace Modules.Threading;

public enum ExecutionPhase
{
    Idle = 0,

    Running,

    Completed,

    Failed,

    Cancelled,

    Timeout
}

public sealed class StepRecord
{
    public string StepName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ExecutionPhase Phase { get; set; } = ExecutionPhase.Idle;

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ExceptionType { get; set; }

    public long DurationMs { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object?>? Tags { get; set; }

    public override string ToString()
        => $"[{StepName}] Phase={Phase} Success={IsSuccess} Duration={DurationMs}ms";
}

public sealed class ExecutionResult
{
    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ExceptionType { get; set; }

    public long TotalDurationMs { get; set; }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime EndTime { get; set; }

    public List<StepRecord> Steps { get; set; } = new();

    public Dictionary<string, object?>? Data { get; set; }

    public void AddStep(StepRecord step)
    {
        Steps.Add(step);
    }

    public StepRecord? FindStep(string stepName)
        => Steps.Find(s => s.StepName == stepName);

    public override string ToString()
        => $"Success={IsSuccess} Duration={TotalDurationMs}ms Steps={Steps.Count}";
}

public sealed class PhaseChangedEventArgs : EventArgs
{
    public ExecutionPhase OldPhase { get; init; }

    public ExecutionPhase NewPhase { get; init; }

    public string? PhaseName { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class StepCompletedEventArgs : EventArgs
{
    public StepRecord Step { get; init; } = null!;
}
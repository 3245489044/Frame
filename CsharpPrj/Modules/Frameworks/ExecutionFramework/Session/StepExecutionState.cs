namespace ExecutionFramework.Session;

public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public class StepExecutionState
{
    private readonly object _lock = new();

    public string StepKey { get; }

    public StepStatus Status { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int ExecutionCount { get; private set; }

    public StepExecutionState(string stepKey)
    {
        StepKey = stepKey ?? throw new ArgumentNullException(nameof(stepKey));
        Status = StepStatus.Pending;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (Status != StepStatus.Pending && Status != StepStatus.Failed)
                throw new InvalidOperationException($"步骤 {StepKey} 当前状态为 {Status}，无法开始执行");

            Status = StepStatus.Running;
            StartedAt = DateTime.UtcNow;
            ExecutionCount++;
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            if (Status != StepStatus.Running)
                throw new InvalidOperationException($"步骤 {StepKey} 当前状态为 {Status}，无法标记完成");

            Status = StepStatus.Completed;
            CompletedAt = DateTime.UtcNow;
        }
    }

    public void Fail(string? errorMessage = null)
    {
        lock (_lock)
        {
            if (Status != StepStatus.Running)
                throw new InvalidOperationException($"步骤 {StepKey} 当前状态为 {Status}，无法标记失败");

            Status = StepStatus.Failed;
            CompletedAt = DateTime.UtcNow;
            ErrorMessage = errorMessage ?? "未知错误";
        }
    }

    public void Skip(string? reason = null)
    {
        lock (_lock)
        {
            if (Status != StepStatus.Pending)
                throw new InvalidOperationException($"步骤 {StepKey} 当前状态为 {Status}，无法跳过");

            Status = StepStatus.Skipped;
            CompletedAt = DateTime.UtcNow;
            ErrorMessage = reason;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            Status = StepStatus.Pending;
            StartedAt = null;
            CompletedAt = null;
            ErrorMessage = null;
        }
    }

    public override string ToString()
    {
        return $"StepState [Key={StepKey}, Status={Status}, Count={ExecutionCount}]";
    }
}

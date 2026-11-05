
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskStatusComponent : IComponent
{
    public enum State
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        Retrying,
        Dead
    }

    public State CurrentState;

    public int RetryCount;

    public int MaxRetries;

    public string? LastError;

    public TaskStatusComponent(int maxRetries = 3)
    {
        CurrentState = State.Pending;
        RetryCount = 0;
        MaxRetries = maxRetries;
        LastError = null;
    }
}

using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskMetricComponent : IComponent
{
    public TimeSpan QueuedDuration;

    public TimeSpan ExecutionDuration;

    public DateTime CreatedAt;

    public DateTime StartedAt;

    public DateTime CompletedAt;
}
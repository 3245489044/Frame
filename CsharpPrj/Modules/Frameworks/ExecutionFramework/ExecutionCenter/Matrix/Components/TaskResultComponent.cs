
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskResultComponent : IComponent
{
    public bool Success;

    public string? ErrorMessage;

    public object? ResultValue;

    public DateTime CompletedAt;
}
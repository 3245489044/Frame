
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskPriorityComponent : IComponent
{
    public int Priority;

    public TaskPriorityComponent(int priority = 2)
    {
        Priority = priority;
    }
}
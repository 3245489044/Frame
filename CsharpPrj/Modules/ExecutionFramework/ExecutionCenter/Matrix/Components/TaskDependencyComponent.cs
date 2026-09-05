
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskDependencyComponent : IComponent
{
    public Entity[] Dependencies;

    public bool AllDependenciesMet;
}
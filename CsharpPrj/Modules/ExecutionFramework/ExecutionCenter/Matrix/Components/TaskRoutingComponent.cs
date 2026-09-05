
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskRoutingComponent : IComponent
{
    public string TargetEnvironment;

    public string HandlerName;

    public TaskRoutingComponent(string handlerName, string targetEnvironment = "CLI")
    {
        HandlerName = handlerName ?? throw new ArgumentNullException(nameof(handlerName));
        TargetEnvironment = targetEnvironment ?? throw new ArgumentNullException(nameof(targetEnvironment));
    }
}

using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ControlTagComponent : IComponent
{
    public HashSet<string> Tags;
}

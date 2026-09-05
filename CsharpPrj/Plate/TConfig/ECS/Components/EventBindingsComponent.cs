
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct EventBindingsComponent : IComponent
{
    public List<EventBindingComponent> Bindings;
}

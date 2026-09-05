
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ControlStateComponent : IComponent
{
    public bool IsCreated;

    public bool IsLayoutApplied;

    public bool IsStyleApplied;

    public bool IsEventsBound;

    public DateTime LastUpdated;
}

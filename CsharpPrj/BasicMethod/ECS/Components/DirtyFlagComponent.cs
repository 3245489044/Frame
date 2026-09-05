
using ECS.Core;

namespace ECS.Components;

public struct DirtyFlagComponent : IComponent
{
    public bool IsDirty;
}
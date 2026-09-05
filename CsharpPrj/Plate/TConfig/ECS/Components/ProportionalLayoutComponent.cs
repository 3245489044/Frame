
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ProportionalLayoutComponent : IComponent
{
    public float Left;

    public float Top;

    public float Width;

    public float Height;

    public bool PreserveAspectRatio;
}

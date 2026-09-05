
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ComputedLayoutComponent : IComponent
{
    public int Left;

    public int Top;

    public int Width;

    public int Height;

    public int ParentWidth;

    public int ParentHeight;
}

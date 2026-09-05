
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

public struct ContainerSizeComponent : IComponent
{
    public int Width;

    public int Height;

    public int Left;

    public int Top;

    public readonly bool IsValid => Width > 0 && Height > 0;
}
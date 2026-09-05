
using ECS.Core;

namespace ECS.Components;

public struct SplitterPanelComponent : IComponent
{
    public string PanelSide;

    public string Orientation;

    public int SplitterWidth;
}

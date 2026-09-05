
#nullable enable
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

public struct ControlInfoComponent : IComponent
{
    public string Name { get; set; }

    public string Text { get; set; }

    public bool Enabled { get; set; }

    public bool Visible { get; set; }
}

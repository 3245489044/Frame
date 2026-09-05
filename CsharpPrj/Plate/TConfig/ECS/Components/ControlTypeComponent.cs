
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ControlTypeComponent : IComponent
{
    public string Type;

    public string Name;

    public string Text;

    public bool Enabled;

    public bool Visible;

    public DateTime EnabledLastUpdated;

    public DateTime VisibleLastUpdated;
}

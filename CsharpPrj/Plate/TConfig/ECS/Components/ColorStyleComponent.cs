
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ColorStyleComponent : IComponent
{
    public string BackColor;

    public string ForeColor;

    public string BorderColor;

    public int BorderWidth;
}


using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct MergedStyleComponent : IComponent
{
    public string BackColor;

    public string ForeColor;

    public string BorderColor;

    public int BorderWidth;

    public string FontFamily;

    public float FontSize;

    public bool Bold;

    public bool Italic;

    public bool Underline;
}

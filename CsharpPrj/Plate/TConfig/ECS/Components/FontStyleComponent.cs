
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct FontStyleComponent : IComponent
{
    public string FontFamily;

    public float FontSize;

    public bool Bold;

    public bool Italic;

    public bool Underline;
}

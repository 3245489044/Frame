
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct ProportionalLayoutComponent : IComponent
{
    public float Left;

    public float Top;

    public float Width;

    public float Height;

    public bool PreserveAspectRatio;

    /// <summary>最小宽度（DIP，96 dpi 参考像素）；0 = 不设下界。布局时按物理控件 DeviceDpi 折算为物理像素。</summary>
    public int MinWidthDip;

    /// <summary>最小高度（DIP）；0 = 不设下界。</summary>
    public int MinHeightDip;

    /// <summary>超额回收优先级：0 = 刚性列（永不低于最小宽）；&gt;0 = 弹性列，数值越大越先让出空间。</summary>
    public int ShrinkPriority;
}

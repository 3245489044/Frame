
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct PixelLayoutComponent : IComponent
{
    public int Left;

    public int Top;

    public int Width;

    public int Height;

    /// <summary>0 = DIP（96 dpi 基准，默认）；1 = 物理像素。DIP 由宿主 DeviceDpi 折算像素。</summary>
    public byte Unit;

    /// <summary>true = layout.intent=fixed（像素直定位）；由 LayoutSystem 结算 ComputedLayoutComponent。</summary>
    public bool IsFixed;
}

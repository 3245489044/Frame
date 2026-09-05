#nullable enable
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace FrontEndDriven.WinForms.Container;

[SupportedOSPlatform("windows")]
// ContainerView：作为普通根容器 Panel 合法使用（MainForm _rootView 宿主）。
// 原「每容器独立 composition」附加轨（Core/AttachCore/RecalculateLayout，配合已删除的 EcsContainerCore）
// 运行期无调用方，属死代码，已随 EcsContainerCore 一并移除。
public class ContainerView : Panel
{
    public ContainerView()
    {
        Dock = DockStyle.Fill;
        // P2 清理：删除恒真判断（_cachedControlStyles 恒非 0）与常量缓存过度设计，直接 SetStyle
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }
}
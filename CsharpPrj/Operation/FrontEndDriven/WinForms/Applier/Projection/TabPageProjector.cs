
#nullable enable

using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// TabPage 投影策略（容器语义：TabManaged）：TabPage 的位置/尺寸/可见性由所属 TabControl 托管（显示矩形 + 选中态），
/// 应用层不写像素 Bounds 也不写 Visible——与 DockProjector 对称（坐标语义由宿主托管而豁免）。
/// 归一后移除坐标键与 Visible 键：快照差异比较仅 Text/Enabled/Name 等非托管属性，
/// 避免「目标像素坐标 vs TabControl 显示矩形」恒差异 与「显式写 Visible 干扰选中语义」。
/// </summary>
public sealed class TabPageProjector : IControlProjectionStrategy
{
    public Dictionary<string, object?> Normalize(Dictionary<string, object?> target, Control control)
    {
        var normalized = new Dictionary<string, object?>(target, System.StringComparer.Ordinal);

        // D3 收敛：坐标键剥离统一经 ProjectionKeys（Tab 显示矩形托管坐标）；Visible 由选中语义托管
        ProjectionKeys.StripCoordinates(normalized);
        normalized.Remove(UIProtocol.Prop_Visible);

        return normalized;
    }

    public bool AppliesTo(ControlDescriptor desc, Control control)
        => (desc != null && string.Equals(desc.Type, "tabpage", System.StringComparison.OrdinalIgnoreCase))
           || control is TabPage;
}

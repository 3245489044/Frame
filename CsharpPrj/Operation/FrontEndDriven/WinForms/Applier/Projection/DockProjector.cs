
#nullable enable

using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// Dock 投影策略（DockProjector）：dock 意图控件（desc 声明 HasDock/Dock 或物理 Dock != None）。
/// 归一后移除坐标键（Left/Top/Width/Height）——坐标由原生 Dock 布局接管，快照比较仅 Text/Visible/Enabled/Name 等
/// 非坐标属性；避免「目标像素坐标 vs Dock 接管后物理坐标」恒差异（旧 skipCoords Dock 豁免）。
/// </summary>
public sealed class DockProjector : IControlProjectionStrategy
{
    public Dictionary<string, object?> Normalize(Dictionary<string, object?> target, Control control)
    {
        var normalized = new Dictionary<string, object?>(target, System.StringComparer.Ordinal);

        // D3 收敛：坐标键剥离统一经 ProjectionKeys（原生 Dock 布局接管坐标）
        ProjectionKeys.StripCoordinates(normalized);

        return normalized;
    }

    public bool AppliesTo(ControlDescriptor desc, Control control)
        => (desc != null && (desc.HasDock || !string.IsNullOrEmpty(desc.Dock)))
        || (control != null && control.Dock != DockStyle.None);
}

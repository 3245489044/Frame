
#nullable enable

using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// 平台约束投影策略（PlatformConstrainedProjector）：ComboBox DropDownList。
/// 归一后移除 height——平台强制高度（DropDownList 模式下 WinForms 自行决定控件高度，应用 Height 不生效），
/// 避免「目标高度 vs 平台实际高度」恒差异；Left/Top/Width 仍参与快照比较（旧 skipCoords ComboBox 豁免的精确化）。
/// </summary>
public sealed class PlatformConstrainedProjector : IControlProjectionStrategy
{
    public Dictionary<string, object?> Normalize(Dictionary<string, object?> target, Control control)
    {
        var normalized = new Dictionary<string, object?>(target, System.StringComparer.Ordinal);

        normalized.Remove(UIProtocol.Prop_Height);

        return normalized;
    }

    public bool AppliesTo(ControlDescriptor desc, Control control)
        => control is ComboBox cb && cb.DropDownStyle == ComboBoxStyle.DropDownList;
}

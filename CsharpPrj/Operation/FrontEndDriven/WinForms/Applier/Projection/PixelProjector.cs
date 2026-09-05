
#nullable enable

using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// 默认像素投影策略（PixelProjector）：比例/像素布局控件。快照含坐标（ECS form 相对），
/// 差异应用时由 PropertyApplier 内 <see cref="LayoutCoordinateResolver.ToParentRelative"/> 唯一换算。
/// 目标尺寸无效（root/非布局，Width/Height ≤ 0）→ 移除坐标键（非布局语义差异，不参与快照比较——
/// 等价旧 skipCoords「目标尺寸无效」豁免）。AppliesTo 恒 false：作为注册表兜底默认，不主动抢占。
/// </summary>
public sealed class PixelProjector : IControlProjectionStrategy
{
    public static readonly PixelProjector Instance = new();

    public Dictionary<string, object?> Normalize(Dictionary<string, object?> target, Control control)
    {
        var normalized = new Dictionary<string, object?>(target, System.StringComparer.Ordinal);

        bool invalidSize = !(target.TryGetValue(UIProtocol.Prop_Width, out var twObj) && twObj is int tw && tw > 0)
            || !(target.TryGetValue(UIProtocol.Prop_Height, out var thObj) && thObj is int th && th > 0);

        if (invalidSize)
        {
            // D3 收敛：坐标键剥离统一经 ProjectionKeys（目标尺寸无效 → 非布局语义差异，不参与快照比较）
            ProjectionKeys.StripCoordinates(normalized);
        }

        return normalized;
    }

    public bool AppliesTo(ControlDescriptor desc, Control control) => false;
}

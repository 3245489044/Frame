
#nullable enable

using System.Collections.Generic;
using System.Windows.Forms;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// 投影策略（R3·豁免参数化，渲染框架架构级重构方案 §四 R3）：
/// 对目标属性做投影前归一（产出参与快照比较的属性集，坐标语义由策略声明），按控件类型/特征判定接管。
/// 新增控件类型/平台差异 = 注册新策略，不改投影核心（WinFormApplier）。
/// </summary>
public interface IControlProjectionStrategy
{
    /// <summary>
    /// 归一目标属性集（BuildPropertyDict 产物，ECS form 相对基准）：返回参与快照比较的属性集。
    /// 坐标语义由策略声明：PixelProjector 含坐标 / DockProjector 移除坐标（原生布局接管）/
    /// PlatformConstrainedProjector 移除 height（平台强制）。
    /// </summary>
    Dictionary<string, object?> Normalize(Dictionary<string, object?> target, Control control);

    /// <summary>策略是否接管该控件（按 desc 类型/意图或 control 特征判定）。</summary>
    bool AppliesTo(ControlDescriptor desc, Control control);
}

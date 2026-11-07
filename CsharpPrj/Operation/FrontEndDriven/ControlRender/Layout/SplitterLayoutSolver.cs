using System;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Components;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Shared.Utils;
using FrameworkAbstractions.ComponentRegistry;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 分割条面板几何解算（布局层自身）：比例 / 方向 / 栏宽真源各自回退，输出相对父客户区的面板矩形。
/// </summary>
internal static class SplitterLayoutSolver
{
    /// <summary>结算分割条面板（Panel1/Panel2）矩形；父尺寸或物理控件缺失 ⇒ false（保持 Pending）。</summary>
    internal static bool TryCompute(
        EntityManager entityManager,
        Entity panelEntity,
        int parentLeft, int parentTop,
        int parentWidth, int parentHeight,
        out ComputedLayoutComponent computed)
    {
        computed = default;

        if (!entityManager.TryGetComponent<SplitterPanelComponent>(panelEntity, out var panel))
            return false;

        if (!entityManager.TryGetComponent<ParentEntityComponent>(panelEntity, out var parentComp)
            || !parentComp.ParentEntity.IsValid)
            return false;

        var splitContainerEntity = parentComp.ParentEntity;

        double r;
        if (entityManager.TryGetComponent<SplitterRatioComponent>(splitContainerEntity, out var ratio)
            // E-12：比例合法域为闭区间 [0,1]；0/1 是拖拽可到达的极端值，不得回退 0.5。
            && ratio.Ratio >= 0.0 && ratio.Ratio <= 1.0)
        {
            r = ratio.Ratio;
        }
        else
        {
            r = 0.5;
        }

        // E1·方向统一：真源 = 物理 Orientation（用户可拖拽/热重排改变），取不到回退配置声明。
        var orientation = TryResolvePhysicalOrientation(splitContainerEntity)
                          ?? ParseOrientation(panel.Orientation);

        // E1·栏宽统一：真源 = 物理 SplitterWidth，取不到回退默认 4（与历史一致）。
        int barWidth = TryResolvePhysicalSplitterWidth(splitContainerEntity) ?? 4;

        bool isPanel1 = string.Equals(panel.PanelSide, "Panel1", StringComparison.OrdinalIgnoreCase);
        var rect = SplitterMath.PanelRect(isPanel1, r, orientation, parentWidth, parentHeight, barWidth);

        computed = new ComputedLayoutComponent
        {
            Left = parentLeft + rect.X,
            Top = parentTop + rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ParentWidth = parentWidth,
            ParentHeight = parentHeight
        };

        return computed.Width > 0 && computed.Height > 0;
    }

    /// <summary>物理 SplitContainer 方向（真源）；取不到物理控件（未注册/测试态）返回 null。</summary>
    private static Orientation? TryResolvePhysicalOrientation(Entity entity)
    {
        var pool = AppServiceProvider.Resolve<ControlPool>();
        return pool?.GetControl(entity) is SplitContainer sc && !sc.IsDisposed
            ? sc.Orientation
            : null;
    }

    /// <summary>物理 Splitter Width（真源）；取不到物理控件返回 null。</summary>
    private static int? TryResolvePhysicalSplitterWidth(Entity entity)
    {
        var pool = AppServiceProvider.Resolve<ControlPool>();
        return pool?.GetControl(entity) is SplitContainer sc && !sc.IsDisposed
            ? sc.SplitterWidth
            : null;
    }

    private static Orientation ParseOrientation(string? orientation)
        => string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase)
            ? Orientation.Horizontal
            : Orientation.Vertical;
}

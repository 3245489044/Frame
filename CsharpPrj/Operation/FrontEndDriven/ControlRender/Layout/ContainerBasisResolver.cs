#nullable enable

using System;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 统一同步「父容器可达成显示区」解析器（重构：托管几何缓存旁路 → 主链路同步推导）。
/// 布局引擎在自顶向下同一趟计算内，为每个父容器解析提供给子控件的可达成显示区：
///   普通容器 → 父自身 ContainerSizeComponent（同步下推，零额外跳）；
///   TabControl → 父 ContainerSizeComponent（同步父链区域）扣减页签条+边框 inset。
/// inset 在 reflow 执行时刻从物理 TabControl.DisplayRectangle 的偏移量同步读取——offset 是
/// 相对稳定物理属性（页签高/边框），不依赖会被「读旧尺寸」污染的缓存快照；
/// 尺寸永远取同步父区，杜绝把滞后尺寸当基准。真源 signals 仍是 Resize，但只触发 reflow。
/// </summary>
public sealed class ContainerBasisResolver
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    public ContainerBasisResolver(EntityManager entityManager, ControlPool controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    public static bool IsTabControlContainer(EntityManager em, Entity entity)
        => em.TryGetComponent<ControlTypeComponent>(entity, out var type)
           && string.Equals(type.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase);

    /// <summary>解析父容器提供给子控件的可达成显示区（全局 form 相对坐标）。</summary>
    /// <returns>父容器才有可达成显示区；父未就绪（无有效 ContainerSize）返回 false。</returns>
    public bool TryResolveChildArea(Entity parentEntity, out int left, out int top, out int width, out int height)
    {
        left = 0; top = 0; width = 0; height = 0;

        if (!_entityManager.TryGetComponent<ContainerSizeComponent>(parentEntity, out var parent) || !parent.IsValid)
            return false;

        left = parent.Left;
        top = parent.Top;
        width = parent.Width;
        height = parent.Height;

        if (!IsTabControlContainer(_entityManager, parentEntity))
            return true;

        // TabControl：可达成显示区 = 父区扣减页签条+边框 inset。只读偏移量（相对稳定），尺寸取同步父区。
        // 物理控件缺失/句柄未就绪 → inset=0 回退父区；TabControl.Resize 触发 reflow 后自愈。
        if (_controlPool.GetControl(parentEntity) is not TabControl tc || !tc.IsHandleCreated)
            return true;

        var dr = tc.DisplayRectangle;
        int clientW = tc.ClientSize.Width;
        int clientH = tc.ClientSize.Height;

        int leftInset   = Math.Max(0, dr.Left);
        int topInset    = Math.Max(0, dr.Top);
        int rightInset  = Math.Max(0, clientW - dr.Right);
        int bottomInset = Math.Max(0, clientH - dr.Bottom);

        left = parent.Left + leftInset;
        top  = parent.Top + topInset;
        width  = Math.Max(0, parent.Width  - leftInset - rightInset);
        height = Math.Max(0, parent.Height - topInset  - bottomInset);
        return true;
    }
}
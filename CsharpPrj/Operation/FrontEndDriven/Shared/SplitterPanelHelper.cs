#nullable enable

using ECS.Components;
using ECS.Core;
using TConfig.ECS.Components;
using FrontEndDriven.Abstractions;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.Shared;

public static class SplitterPanelHelper
{
    public static void ResolveSplitterParent(
        EntityManager entityManager,
        in Entity parentEntity,
        out Entity resolvedParent,
        out string? targetPanel)
    {
        if (entityManager.HasComponent<SplitterPanelComponent>(parentEntity))
        {
            var panel = entityManager.GetComponent<SplitterPanelComponent>(parentEntity);
            // 与 WinFormsControlAdapter.GetSplitterPanel 的判定标准一致（OrdinalIgnoreCase），避免文化差异导致面板侧识别漂移。
            targetPanel = string.Equals(panel.PanelSide, UIProtocol.SplitterPanel1, StringComparison.OrdinalIgnoreCase)
                ? UIProtocol.SplitterPanel1
                : UIProtocol.SplitterPanel2;

            if (entityManager.TryGetComponent<ParentEntityComponent>(parentEntity, out var parentComp)
                && parentComp.ParentEntity.IsValid)
            {
                resolvedParent = parentComp.ParentEntity;
                return;
            }
        }

        resolvedParent = parentEntity;
        targetPanel = null;
    }

    public static bool TryAddControl(
        IControlAdapter adapter,
        object parentControl,
        string? targetPanel,
        object childControl)
    {
        if (adapter == null || parentControl == null || childControl == null)
            return false;

        // M2 挂载成败透传：返回值为 adapter.AddControl 的真实结果（不再恒 true），
        // 使「挂载失败」沿调用链上抛到 ApplyRelations 统计层，杜绝「调用即成功」的假绿。
        if (!string.IsNullOrEmpty(targetPanel))
        {
            var target = adapter.GetSplitterPanel(parentControl, targetPanel);
            if (target == null)
                return false;
            return adapter.AddControl(target, childControl);
        }

        return adapter.AddControl(parentControl, childControl);
    }
}

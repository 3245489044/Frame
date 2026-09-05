#nullable enable

using System;
using System.Drawing;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// ECS 父原点解析（单一权威，SingleImplementation）：布局物理投影的父基准唯一来源。
/// 消除「物理父链换算（ToParentRelative，依赖 parent 已挂载）」与「ECS 父原点（ResolveParentOrigin）」两套并存实现的基准分叉。
/// 语义：物理父相对 = ComputedLayout(E) − ECS父原点。
///   - 无 ECS 父（scope 根）→ HostBasisComponent（宿主全局 form 相对原点；缺省 (0,0)）；
///   - 有 ECS 父 → 父实体 ComputedLayoutComponent.Left/Top（全局 form 相对，含宿主偏移）。
/// 与物理父链无关 → 挂载时序（root 未挂宿主）不影响坐标正确性，首渲即正确。
/// </summary>
public static class EcsParentOrigin
{
    /// <summary>返回实体在 ECS 布局中的父原点（全局 form 相对坐标）。</summary>
    public static Point Resolve(EntityManager em, Entity entity)
    {
        if (em == null) throw new ArgumentNullException(nameof(em));

        // scope 根（无 ECS 父）：原点 = 宿主基准（HostBasisComponent）
        if (!em.TryGetComponent<ParentEntityComponent>(entity, out var parentComp)
            || !parentComp.ParentEntity.IsValid)
        {
            if (em.TryGetComponent<HostBasisComponent>(entity, out var basis))
                return new Point(basis.Left, basis.Top);
            return Point.Empty;
        }

        // 有 ECS 父：原点 = 父实体 ComputedLayout（全局 form 相对）
        var parent = parentComp.ParentEntity;
        if (parent.IsValid && em.TryGetComponent<ComputedLayoutComponent>(parent, out var parentCl))
            return new Point(parentCl.Left, parentCl.Top);

        return Point.Empty;
    }
}

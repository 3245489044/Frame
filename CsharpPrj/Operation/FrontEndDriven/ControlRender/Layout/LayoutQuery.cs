#nullable enable

using System.Windows.Forms;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 布局查询辅助（D1/D2/D3 收敛）：scope 判定、dock 意图判定、物理父链深度——单一权威，
/// 消除 LayoutSystem/LayoutProjector 中的手写副本（此前 dock 意图判定重复 5 处、IsInScope/PhysicalDepth 各重复 2 处）。
/// </summary>
public static class LayoutQuery
{
    /// <summary>dock 意图（含非 Fill；dock 由原生布局接管，非 Fill 不参与像素结算）。</summary>
    public static bool IsDockIntent(EntityManager em, Entity entity)
        => em.TryGetComponent<LayoutIntentComponent>(entity, out var intent)
            && string.Equals(intent.Intent, "dock", StringComparison.OrdinalIgnoreCase);

    /// <summary>dock=Fill 意图（布局结算候选——与 LayoutSystem.TryComputeEntityLayout 结算条件对齐，排除非 Fill 空转）。</summary>
    public static bool IsFillDock(EntityManager em, Entity entity)
        => IsDockIntent(em, entity)
            && em.TryGetComponent<LayoutIntentComponent>(entity, out var intent)
            && string.Equals(intent.Dock, "Fill", StringComparison.OrdinalIgnoreCase);

    /// <summary>原生接管意图（dock/anchor，不参与像素布局投影）。</summary>
    public static bool IsNativeIntent(EntityManager em, Entity entity)
        => IsDockIntent(em, entity)
            || (em.TryGetComponent<LayoutIntentComponent>(entity, out var intent)
                && string.Equals(intent.Intent, "anchor", StringComparison.OrdinalIgnoreCase));

    /// <summary>实体是否属于指定 scope（Ordinal 比较）。</summary>
    public static bool IsInScope(EntityManager em, Entity entity, string scope)
        => em.TryGetComponent<ScopeComponent>(entity, out var scopeComp)
            && string.Equals(scopeComp.Scope, scope, StringComparison.Ordinal);

    /// <summary>物理父链深度（父先子后投影排序基准）。</summary>
    public static int PhysicalDepth(Control? control)
    {
        int depth = 0;
        for (Control? c = control; c != null; c = c.Parent)
            depth++;
        return depth;
    }
}

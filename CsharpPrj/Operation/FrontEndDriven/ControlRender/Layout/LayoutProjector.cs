#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.Abstractions;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Shared.Utils;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 布局投影器（LayoutProjector）：对 scope 做「仅 Bounds」差异投影——布局重排的唯一物理写入出口。
/// 与内容投影（WinFormApplier.Apply / ApplySnapshot）彻底分离：
///   不重挂父链、不重放属性、不重置选中态、不跑全局审计，
///   也不写内容 AppliedSnapshotRegistry（以物理控件 Bounds 为基线 → 每次重排只对实际变化的实体 SetBounds，永不污染内容差异快照）。
/// 坐标（R1·纯 ECS）：ComputedLayoutComponent 为全局 form 相对（ECS 原生语义，含宿主偏移），
/// 父相对 = cl(E) - cl(ECS父)（父原点取自 ECS 父实体 ComputedLayout / root 的 HostBasisComponent），不读物理父链 → 确定性、幂等、首遍即正确。
/// 原生接管（dock/anchor）实体跳过——由 WinForms 原生布局引擎随宿主缩放，无需框架投影。
/// 分割容器：尺寸变化时按 SplitterRatio 重算 SplitterDistance（面板随之缩放，面板内子 scope 经宿主 Resize 自触发重排）。
/// </summary>
public sealed class LayoutProjector
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IControlAdapter _adapter;

    public LayoutProjector(EntityManager entityManager, ControlPool controlPool, IControlAdapter adapter)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <summary>
    /// 对 scope 内比例/分割实体做 bounds-only 差异投影，返回实际变更实体数。
    /// R2·收敛循环：单次重排内部迭代至无变化——父先子后投影后，子 ToParentRelative 换算基准（父当前物理位置）
    /// 在下一遍已被校正，故首遍读取的旧父位置在后续遍被修正，彻底消除「第一次拉伸错、第二次才正常」的首遍收敛缺陷。
    /// </summary>
    public int ProjectScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return 0;

        int totalChanged = 0;
        for (int pass = 0; pass < LayoutConvergencePolicy.MaxProjectionPasses; pass++)
        {
            int changedThisPass = ProjectScopeOnce(scope);
            totalChanged += changedThisPass;
            if (changedThisPass == 0)
                break;
        }

        return totalChanged;
    }

    private int ProjectScopeOnce(string scope)
    {
        int changed = 0;

        // 父先子后：父控件 SetBounds 就位后，子 ToParentRelative 换算基准（父当前物理位置）才正确。
        var proportional = new List<Entity>();
        foreach (var entity in _entityManager.GetEntities<ProportionalLayoutComponent>())
        {
            if (!LayoutQuery.IsInScope(_entityManager, entity, scope)
                || LayoutQuery.IsNativeIntent(_entityManager, entity))
                continue;
            if (!_entityManager.HasComponent<ComputedLayoutComponent>(entity))
                continue;
            proportional.Add(entity);
        }
        proportional.Sort((a, b) => LayoutQuery.PhysicalDepth(GetControl(a)).CompareTo(LayoutQuery.PhysicalDepth(GetControl(b))));

        foreach (var entity in proportional)
        {
            var control = GetControl(entity);
            if (control == null || control.IsDisposed)
                continue;
            if (!_entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var cl))
                continue;

            // R1·纯 ECS 坐标换算：父相对 = cl(E) - cl(ECS父)。父原点取自 ECS 父实体 ComputedLayout
            //（SplitterPanel 实体自带 ComputedLayout，天然覆盖 splitter 面板语义；根实体父 = 宿主，原点 = root 的 HostBasisComponent.Left/Top）。
            // 不再读物理父链 → 投影结果由 ECS 唯一决定，确定性/幂等，首遍即正确（不依赖物理收敛）。
            var (originX, originY) = ResolveParentOrigin(entity);
            int relX = cl.Left - originX;
            int relY = cl.Top - originY;

            // Pillar A·收敛判定下沉 ECS：以「已投影 ECS bounds 标记」判定是否已就位，不再读物理当前值——
            // 消除「物理读回 → SetBounds → ResumeLayout 二次调整 → 物理≠ECS → 再写」的振荡环；
            // 已就位则零 SetBounds（无冗余、不扰动）。同 ECS 两次投影零写入（幂等）。
            if (IsProjected(entity, relX, relY, cl.Width, cl.Height))
                continue;

            _adapter.SetBounds(control, relX, relY, cl.Width, cl.Height);
            MarkProjected(entity, relX, relY, cl.Width, cl.Height);
            changed++;
        }

        // 分割容器：尺寸变化时按 SplitterRatio 重算 SplitterDistance（物理基线差异，容差 1px）。
        foreach (var entity in _entityManager.GetEntities<SplitterRatioComponent>())
        {
            if (!LayoutQuery.IsInScope(_entityManager, entity, scope))
                continue;
            if (!_entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var cl))
                continue;

            var control = GetControl(entity);
            if (control is not SplitContainer sc || sc.IsDisposed)
                continue;

            // 防御并发：枚举到 splitter 组件后可能已被并发销毁移除，Try 读避免抛异常。
            if (!_entityManager.TryGetComponent<SplitterRatioComponent>(entity, out var ratio))
                continue;

            // E1·分割基准按方向选取（SplitterMath 单一权威）：Vertical 沿宽、Horizontal 沿高，
            // 与 Ports.CalculateSplitterDistance / LayoutSystem.TryComputeSplitterPanelLayout 一致，
            // 消除 Math.Max 对「比高还宽的横向分割/比宽还高的纵向分割」取错基准维度的缺陷。
            int distance = SplitterMath.Distance(ratio.Ratio, sc.Orientation, cl.Width, cl.Height);
            if (distance <= 0)
                continue;
            if (Math.Abs(_adapter.GetSplitterDistance(sc) - distance) <= 1)
                continue;

            _adapter.SetSplitterDistance(sc, distance);
            changed++;
        }

        return changed;
    }

    private Control? GetControl(Entity entity) => _controlPool.GetControl(entity) as Control;

    /// <summary>Pillar A·幂等判定：实体是否已投影到给定 ECS bounds（读 ProjectedLayoutComponent 标记，不读物理）。</summary>
    private bool IsProjected(Entity entity, int left, int top, int width, int height)
        => _entityManager.TryGetComponent<ProjectedLayoutComponent>(entity, out var p)
            && p.Left == left && p.Top == top && p.Width == width && p.Height == height;

    /// <summary>Pillar A·写入投影标记（SetBounds 后记录本次投影的 ECS bounds）。</summary>
    private void MarkProjected(Entity entity, int left, int top, int width, int height)
        => _entityManager.AddOrSetComponent(entity, new ProjectedLayoutComponent
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        });

    /// <summary>
    /// R1·纯 ECS 父原点：实体物理父的全局 form 原点，取自 ECS 而非物理树
    /// （单一权威见 <see cref="EcsParentOrigin.Resolve"/>）。与物理父链无关 → 挂载时序不影响坐标正确性。
    /// </summary>
    private (int X, int Y) ResolveParentOrigin(Entity entity)
    {
        var origin = EcsParentOrigin.Resolve(_entityManager, entity);
        return (origin.X, origin.Y);
    }
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ECS.Bridge;
using ECS.Core;
using TConfig.ECS.Components;
using FrontEndDriven.ECS.Components;
using FrontEndDriven;
using FrontEndDriven.ControlRender.Diagnostics;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;
using ECS.Components;

namespace FrontEndDriven.ECS.Systems;

public class LayoutSystem
{
    private readonly EntityManager _entityManager;

    /// <summary>基准解析（父容器可达成显示区 / root 自身基准 / 物理 DPI）；由本系统构造注入解析器与控件池。</summary>
    private readonly LayoutBasisReader _basis;

    /// <summary>注入统一同步父容器可达成显示区解析器（TabControl 自动扣页签条+边框 inset）；测试态可传 null 走父 ContainerSize 单跳兜底。</summary>
    public LayoutSystem(EntityManager entityManager, ContainerBasisResolver? basisResolver, ControlPool? controlPool = null)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _basis = new LayoutBasisReader(_entityManager, basisResolver, controlPool);
    }

    public void Update()
    {
        var pendingEntities = GetLayoutPendingEntities().ToList();

        if (pendingEntities.Count == 0)
            return;

        // 收敛循环：父先子后由 ContainerBasisResolver 在单趟内保证（父基准未就绪的子以 Pending 保持，
        // 收敛循环按深度推进重试）。不再需要独立的「深度排序预结算」趟——同一批实体由收敛循环重算至无变化。
        for (int pass = 0; pass < LayoutConvergencePolicy.MaxLayoutPasses; pass++)
        {
            // 每趟先按父容器重算「比例 + 最小像素」分配：父尺寸在本趟追平后，子分配下一趟即收敛。
            ReconcileMinAllocations();

            bool anyComputedThisPass = false;

            foreach (var entity in GetLayoutPendingEntities())
            {
                if (TryComputeEntityLayout(entity))
                    anyComputedThisPass = true;
            }

            if (!anyComputedThisPass)
                break;
        }

        // M1·结果契约：每次布局计算后断言收敛（缺失/0 尺寸实体显式 Warning，Info 常开）
        LayoutConvergenceReport.Report(_entityManager);
    }

    /// <summary>
    /// 布局计算候选集：比例布局实体 + dock 意图实体（A2·布局真相缺失修复——
    /// dock=Fill 容器此前无 ProportionalLayoutComponent 而不在计算集合内 → 永无
    /// ComputedLayoutComponent → ECS 真源 0x0）。anchor 意图由原生 Anchor 接管，不参与 ECS 尺寸计算。
    /// </summary>
    private IEnumerable<Entity> GetLayoutPendingEntities()
    {
        return _entityManager.GetEntities<ProportionalLayoutComponent, PendingLayoutComponent>()
            .Concat(_entityManager.GetEntities<LayoutIntentComponent, PendingLayoutComponent>()
                .Where(e => LayoutQuery.IsFillDock(_entityManager, e)))
            .Concat(_entityManager.GetEntities<PixelLayoutComponent, PendingLayoutComponent>()
                .Where(e => LayoutQuery.IsFixedPixels(_entityManager, e)))
            .Distinct();
    }

    private bool TryComputeEntityLayout(Entity entity)
    {
        if (!_basis.TryGetParentContainerSize(entity,
            out var parentLeft, out var parentTop, out var parentWidth, out var parentHeight))
        {
            return false;
        }

        // P1·固定布局：layout.intent=fixed 的 DIP 声明值按宿主 DPI 折算像素，
        // 参与 ComputedLayout 真源（此前固定控件只有 PixelLayout 组件、永不生 ComputedLayout，
        // 物理尺寸由 WinForms 默认值决定 → 无法统一改版式）。
        if (LayoutQuery.IsFixedPixels(_entityManager, entity))
            return TryComputeFixedLayout(entity, parentLeft, parentTop, parentWidth, parentHeight);

        ComputedLayoutComponent computed;
        if (IsSplitterPanelEntity(entity))
        {
            if (!SplitterLayoutSolver.TryCompute(_entityManager, entity, parentLeft, parentTop, parentWidth, parentHeight, out computed))
            {
                return false;
            }
        }
        else if (LayoutQuery.IsDockIntent(_entityManager, entity))
        {
            // A2·dock 真源：dock=Fill 实体（容器默认布局语义）按父客户区结算（与 WinForms Dock=Fill 对齐），
            // 空载容器（autoLoad=false）在 ECS 真源中即获得尺寸，不再静默 0x0。
            // 父尺寸未就绪 → 返回 false 保持 Pending，由收敛循环在父面板结算后重试（避免写入 0 尺寸假真相）。
            // 非 Fill dock（Top/Bottom/Left/Right）由原生 Dock 排布，尺寸真源不在此结算（保持现状）。
            if (!LayoutQuery.IsFillDock(_entityManager, entity)
                || parentWidth <= 0 || parentHeight <= 0)
            {
                return false;
            }

            computed = new ComputedLayoutComponent
            {
                Left = parentLeft,
                Top = parentTop,
                Width = parentWidth,
                Height = parentHeight,
                ParentWidth = parentWidth,
                ParentHeight = parentHeight
            };
        }
        else
        {
            var propLayout = _entityManager.GetComponent<ProportionalLayoutComponent>(entity);

            // 「比例 + 最小像素」双约束：有分配记录时优先采用（含最小宽下界与超额回收结果）；
            // 无最小配置的实体不产生记录 → 走原公式，逐值不变（其它面板零影响）。
            if (_entityManager.TryGetComponent<LayoutAllocationComponent>(entity, out var allocation))
            {
                computed = new ComputedLayoutComponent
                {
                    Left = parentLeft + allocation.Left,
                    Top = parentTop + allocation.Top,
                    Width = allocation.Width,
                    Height = allocation.Height,
                    ParentWidth = parentWidth,
                    ParentHeight = parentHeight
                };
            }
            else
            {
                computed = new ComputedLayoutComponent
                {
                    Left = parentLeft + (int)(propLayout.Left * parentWidth),
                    Top = parentTop + (int)(propLayout.Top * parentHeight),
                    Width = (int)(propLayout.Width * parentWidth),
                    Height = (int)(propLayout.Height * parentHeight),
                    ParentWidth = parentWidth,
                    ParentHeight = parentHeight
                };
            }
        }

        _entityManager.AddOrSetComponent(entity, computed);

        if (IsContainerEntity(entity) || IsSplitterPanelEntity(entity))
        {
            AddOrSetContainerSize(entity, computed.Left, computed.Top, computed.Width, computed.Height);
        }


        _entityManager.RemoveComponent<PendingLayoutComponent>(entity);
        _entityManager.AddComponent(entity, new LayoutAppliedComponent());

        return true;
    }

    /// <summary>
    /// 固定布局结算：PixelLayout 声明值（DIP）→ 物理像素（宿主 DeviceDpi，控件未就绪回退父 DPI，再回退 96）。
    /// 与比例布局共用 ComputedLayout 真源，供投影/快照/检查统一消费。
    /// </summary>
    private bool TryComputeFixedLayout(Entity entity, int parentLeft, int parentTop, int parentWidth, int parentHeight)
    {
        if (parentWidth <= 0 || parentHeight <= 0)
            return false;

        var pixel = _entityManager.GetComponent<PixelLayoutComponent>(entity);

        var dpi = _basis.ResolveDeviceDpi(entity);
        if (dpi <= 0 && _entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parent)
            && parent.ParentEntity.IsValid)
        {
            dpi = _basis.ResolveDeviceDpi(parent.ParentEntity);
        }

        if (dpi <= 0)
            dpi = ProportionalLayoutPolicy.ReferenceDpi;

        int ToPixels(int value) => pixel.Unit == 1 ? value : ProportionalLayoutPolicy.ScaleDip(value, dpi);

        var computed = new ComputedLayoutComponent
        {
            Left = parentLeft + ToPixels(pixel.Left),
            Top = parentTop + ToPixels(pixel.Top),
            Width = ToPixels(pixel.Width),
            Height = ToPixels(pixel.Height),
            ParentWidth = parentWidth,
            ParentHeight = parentHeight
        };

        _entityManager.AddOrSetComponent(entity, computed);
        _entityManager.RemoveComponent<PendingLayoutComponent>(entity);
        _entityManager.AddComponent(entity, new LayoutAppliedComponent());
        return true;
    }

    /// <summary>
    /// 「比例 + 最小像素」双约束分配：按父实体分组重算，写入 <see cref="LayoutAllocationComponent"/>。
    /// 仅当组内存在 MinWidthDip &gt; 0 才计算；无最小配置的父容器保持原比例路径（逐值不变）。
    /// </summary>
    private void ReconcileMinAllocations()
    {
        var groups = new Dictionary<Entity, List<Entity>>();

        foreach (var entity in _entityManager.GetEntities<ProportionalLayoutComponent>())
        {
            if (IsSplitterPanelEntity(entity))
                continue;
            if (_entityManager.HasComponent<LayoutIntentComponent>(entity)
                || LayoutQuery.IsNativeIntent(_entityManager, entity))
                continue;
            if (!_entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parent)
                || !parent.ParentEntity.IsValid)
                continue;

            if (!groups.TryGetValue(parent.ParentEntity, out var siblings))
            {
                siblings = new List<Entity>();
                groups[parent.ParentEntity] = siblings;
            }

            siblings.Add(entity);
        }

        foreach (var group in groups)
            ReconcileSiblingGroup(group.Value);
    }

    private void ReconcileSiblingGroup(List<Entity> siblings)
    {
        bool anyMin = false;
        foreach (var sibling in siblings)
        {
            if (_entityManager.GetComponent<ProportionalLayoutComponent>(sibling).MinWidthDip > 0)
            {
                anyMin = true;
                break;
            }
        }

        if (!anyMin)
        {
            foreach (var sibling in siblings)
            {
                if (_entityManager.HasComponent<LayoutAllocationComponent>(sibling))
                    _entityManager.RemoveComponent<LayoutAllocationComponent>(sibling);
            }

            return;
        }

        if (!_basis.TryGetParentContainerSize(siblings[0], out _, out _, out var parentWidth, out var parentHeight)
            || parentWidth <= 0 || parentHeight <= 0)
            return;

        int dpi = 0;
        foreach (var sibling in siblings)
            dpi = Math.Max(dpi, _basis.ResolveDeviceDpi(sibling));

        if (dpi <= 0 && _entityManager.TryGetComponent<ParentEntityComponent>(siblings[0], out var parentComp))
            dpi = _basis.ResolveDeviceDpi(parentComp.ParentEntity);

        if (dpi <= 0)
            dpi = ProportionalLayoutPolicy.ReferenceDpi;

        var slots = new List<ProportionalLayoutPolicy.Slot>(siblings.Count);
        foreach (var sibling in siblings)
        {
            var prop = _entityManager.GetComponent<ProportionalLayoutComponent>(sibling);
            slots.Add(new ProportionalLayoutPolicy.Slot(
                sibling,
                prop.Left,
                prop.Top,
                prop.Width,
                prop.Height,
                prop.MinWidthDip,
                prop.MinHeightDip,
                prop.ShrinkPriority));
        }

        foreach (var allocation in ProportionalLayoutPolicy.Allocate(parentWidth, parentHeight, dpi, slots))
            StoreAllocation(allocation);
    }

    /// <summary>写分配记录（值未变化则零写入，保证重复重排幂等）。</summary>
    private void StoreAllocation(ProportionalLayoutPolicy.Result allocation)
    {
        if (_entityManager.TryGetComponent<LayoutAllocationComponent>(allocation.Entity, out var existing)
            && existing.Left == allocation.Left
            && existing.Top == allocation.Top
            && existing.Width == allocation.Width
            && existing.Height == allocation.Height
            && existing.NominalWidth == allocation.NominalWidth
            && existing.MinWidthPx == allocation.MinWidthPx
            && existing.OverflowPx == allocation.OverflowPx
            && string.Equals(existing.Effective, allocation.Effective, StringComparison.Ordinal))
        {
            return;
        }

        _entityManager.AddOrSetComponent(allocation.Entity, new LayoutAllocationComponent
        {
            Left = allocation.Left,
            Top = allocation.Top,
            Width = allocation.Width,
            Height = allocation.Height,
            NominalWidth = allocation.NominalWidth,
            MinWidthPx = allocation.MinWidthPx,
            Effective = allocation.Effective,
            OverflowPx = allocation.OverflowPx
        });

        if (allocation.OverflowPx > 0)
        {
            FrontEndDrivenLog.Log("Layout_MinWidth_Overflow", new
            {
                effective = allocation.Effective,
                nominalWidth = allocation.NominalWidth,
                minWidthPx = allocation.MinWidthPx,
                allocatedWidth = allocation.Width,
                overflowPx = allocation.OverflowPx
            });
        }
    }

    private bool IsContainerEntity(Entity entity)
    {
        if (!_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp))
            return false;

        return FrontEndDrivenConstants.ContainerTypes.Contains(typeComp.Type ?? string.Empty);
    }

    private bool IsSplitterPanelEntity(Entity entity)
    {
        return _entityManager.HasComponent<SplitterPanelComponent>(entity);
    }

    private void AddOrSetContainerSize(Entity entity, int left, int top, int width, int height)
    {
        var newSize = new ContainerSizeComponent
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };

        if (_entityManager.TryGetComponent<ContainerSizeComponent>(entity, out var current)
            && current.Left == left && current.Top == top
            && current.Width == width && current.Height == height)
        {
            return;
        }

        _entityManager.AddOrSetComponent(entity, newSize);
    }

    public void RecalculateAll()
    {
        // P1·固定布局随全量重算一并置脏：DPI/宿主基准变化时 DIP 声明值需重新折算像素。
        var entities = _entityManager.GetEntities<LayoutAppliedComponent>()
            .Where(e => _entityManager.HasComponent<ProportionalLayoutComponent>(e)
                || LayoutQuery.IsFixedPixels(_entityManager, e))
            .ToList();

        foreach (var entity in entities)
        {
            _entityManager.RemoveComponent<LayoutAppliedComponent>(entity);
            if (!_entityManager.HasComponent<PendingLayoutComponent>(entity))
            {
                _entityManager.AddComponent(entity, new PendingLayoutComponent());
            }
        }

        var containerEntities = _entityManager.GetEntities<ContainerSizeComponent>();
        foreach (var entity in containerEntities)
        {
            if (!_entityManager.HasComponent<ParentEntityComponent>(entity))
                continue;

            _entityManager.RemoveComponent<ContainerSizeComponent>(entity);
        }

        Update();
    }

    /// <summary>
    /// Phase 2·scope 感知增量重算：仅把 <paramref name="scope"/> 内的布局实体（比例 + dock=Fill）置脏并重算，
    /// 防止「Content 宿主重排误重算 MainLayout splitter」等跨 scope 误伤（宿主 Resize 触发重排时的正确性前提——
    /// 否则 Content 重排会用旧 ECS SplitterRatio 覆盖物理拖拽结果，重演 M1「拖拽被回滚」缺陷）。
    /// scope 为空/空串时回退全量 <see cref="RecalculateAll"/>（兼容旧语义）。
    /// </summary>
    public void RecalculateScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            RecalculateAll();
            return;
        }

        foreach (var entity in GetScopeLayoutEntities(scope))
        {
            if (!_entityManager.HasComponent<LayoutAppliedComponent>(entity))
                continue;

            _entityManager.RemoveComponent<LayoutAppliedComponent>(entity);
            if (!_entityManager.HasComponent<PendingLayoutComponent>(entity))
            {
                _entityManager.AddComponent(entity, new PendingLayoutComponent());
            }
        }

        // 本 scope 容器清 ContainerSize，强制按新基准重算（中间容器尺寸不作为跨 scope 的独立真源）。
        foreach (var entity in _entityManager.GetEntities<ContainerSizeComponent>())
        {
            if (!LayoutQuery.IsInScope(_entityManager, entity, scope))
                continue;

            if (!_entityManager.HasComponent<ParentEntityComponent>(entity))
                continue;

            _entityManager.RemoveComponent<ContainerSizeComponent>(entity);
        }

        Update();
    }

    /// <summary>scope 内布局实体（比例 + dock=Fill），供 scope 感知重算。</summary>
    private IEnumerable<Entity> GetScopeLayoutEntities(string scope)
    {
        foreach (var entity in _entityManager.GetEntities<ProportionalLayoutComponent>())
        {
            if (LayoutQuery.IsInScope(_entityManager, entity, scope))
                yield return entity;
        }

        // R1·仅 dock=Fill 参与结算：候选集合与 TryComputeEntityLayout 结算条件对齐，
        // 排除非 Fill dock（原生 Dock 接管）被错误打上 Pending 而永不结算的空转。
        foreach (var entity in _entityManager.GetEntities<LayoutIntentComponent>())
        {
            if (LayoutQuery.IsFillDock(_entityManager, entity)
                && LayoutQuery.IsInScope(_entityManager, entity, scope))
            {
                yield return entity;
            }
        }

        // P1·固定布局：scope 感知重排同样覆盖 fixed 实体（父基准/DPI 变化时重新折算）。
        foreach (var entity in _entityManager.GetEntities<PixelLayoutComponent>())
        {
            if (LayoutQuery.IsFixedPixels(_entityManager, entity)
                && LayoutQuery.IsInScope(_entityManager, entity, scope))
            {
                yield return entity;
            }
        }
    }
}

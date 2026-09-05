
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using TConfig.ECS.Components;
using FrontEndDriven.ECS.Components;
using FrontEndDriven;
using FrontEndDriven.ControlRender.Diagnostics;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Shared.Utils;
using FrameworkAbstractions.ComponentRegistry;
using ECS.Components;

namespace FrontEndDriven.ECS.Systems;

public class LayoutSystem
{
    private readonly EntityManager _entityManager;

    private readonly ContainerBasisResolver? _basisResolver;

    /// <summary>注入统一同步父容器可达成显示区解析器（TabControl 自动扣页签条+边框 inset）；测试态可传 null 走父 ContainerSize 单跳兜底。</summary>
    public LayoutSystem(EntityManager entityManager, ContainerBasisResolver? basisResolver)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _basisResolver = basisResolver;
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
            .Distinct();
    }

    private bool TryComputeEntityLayout(Entity entity)
    {
        if (!TryGetParentContainerSize(entity,
            out var parentLeft, out var parentTop, out var parentWidth, out var parentHeight))
        {
            return false;
        }

        ComputedLayoutComponent computed;
        if (IsSplitterPanelEntity(entity))
        {
            if (!TryComputeSplitterPanelLayout(entity, parentLeft, parentTop, parentWidth, parentHeight, out computed))
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

        _entityManager.AddOrSetComponent(entity, computed);

        if (IsContainerEntity(entity) || IsSplitterPanelEntity(entity))
        {
            AddOrSetContainerSize(entity, computed.Left, computed.Top, computed.Width, computed.Height);
        }


        _entityManager.RemoveComponent<PendingLayoutComponent>(entity);
        _entityManager.AddComponent(entity, new LayoutAppliedComponent());

        return true;
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

    private bool TryComputeSplitterPanelLayout(
        Entity panelEntity,
        int parentLeft, int parentTop,
        int parentWidth, int parentHeight,
        out ComputedLayoutComponent computed)
    {
        computed = default;

        if (!_entityManager.TryGetComponent<SplitterPanelComponent>(panelEntity, out var panel))
            return false;

        if (!_entityManager.TryGetComponent<ParentEntityComponent>(panelEntity, out var parentComp)
            || !parentComp.ParentEntity.IsValid)
            return false;

        var splitContainerEntity = parentComp.ParentEntity;

        double r;
        if (_entityManager.TryGetComponent<SplitterRatioComponent>(splitContainerEntity, out var ratio)
            && ratio.Ratio > 0.0 && ratio.Ratio < 1.0)
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

    private bool TryGetParentContainerSize(Entity entity,
        out int parentLeft, out int parentTop, out int width, out int height)
    {
        parentLeft = 0;
        parentTop = 0;
        width = 0;
        height = 0;

        // root 实体（树根，无 ParentEntityComponent）无法沿父链取基准——以自身 HostBasisComponent
        // （宿主基准，HostBasisSystem 决议）优先、ContainerSizeComponent（配置基准）兜底作为布局基准，
        // 使 root 获得 ComputedLayoutComponent（修复 root 物理控件停在 WinForms 默认 200x100、
        // 整棵渲染树被父边界裁剪到左上角 200x100 的断链：布局基准就绪 → 快照携带尺寸 → ApplyProperties 投影）。
        // 非 root 实体无 ParentEntityComponent 且无自身容器基准时行为不变（返回 false，保持 Pending）。
        if (!_entityManager.TryGetComponent<ParentEntityComponent>(entity, out _))
        {
            return TryResolveSelfBasis(entity, out parentLeft, out parentTop, out width, out height);
        }

        // root 之外的实体：父容器可达成显示区一律经 ContainerBasisResolver（单一权威）解析——
        // 普通容器直通父 ContainerSize，TabControl 自动扣页签条+边框 inset，消除手写父链多跳遍历重复。
        if (!_entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parentComp)
            || !parentComp.ParentEntity.IsValid)
        {
            return false;
        }

        var parentEntity = parentComp.ParentEntity;

        if (_basisResolver != null)
        {
            return _basisResolver.TryResolveChildArea(parentEntity, out parentLeft, out parentTop, out width, out height);
        }

        // 无 resolver（测试态单参构造）：退化为父 ContainerSize 单跳直读，保持旧语义。
        if (_entityManager.TryGetComponent<ContainerSizeComponent>(parentEntity, out var containerSize) && containerSize.IsValid)
        {
            parentLeft = containerSize.Left;
            parentTop = containerSize.Top;
            width = containerSize.Width;
            height = containerSize.Height;
            return true;
        }

        return false;
    }

    /// <summary>root 自身基准（无 ECS 父）：HostBasisComponent 优先，ContainerSizeComponent 兜底。</summary>
    private bool TryResolveSelfBasis(Entity entity,
        out int left, out int top, out int width, out int height)
    {
        left = 0; top = 0; width = 0; height = 0;

        if (_entityManager.TryGetComponent<HostBasisComponent>(entity, out var selfBasis) && selfBasis.IsValid)
        {
            left = selfBasis.Left; top = selfBasis.Top; width = selfBasis.Width; height = selfBasis.Height;
            return true;
        }

        if (_entityManager.TryGetComponent<ContainerSizeComponent>(entity, out var selfSize) && selfSize.IsValid)
        {
            left = selfSize.Left; top = selfSize.Top; width = selfSize.Width; height = selfSize.Height;
            return true;
        }

        return false;
    }

    public void RecalculateAll()
    {
        var entities = _entityManager.GetEntities<
            ProportionalLayoutComponent,
            LayoutAppliedComponent>();

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
    }
}

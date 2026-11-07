using System;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Components;
using ECS.Core;
using FrontEndDriven.ECS.Components;
using FrameworkAbstractions.ComponentRegistry;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 布局基准解析（布局层自身）：父容器可达成显示区、root 自身基准、物理控件 DPI。
/// <para>基准解析器与控件池由构造注入；缺省时逐级回退（父 ContainerSize 单跳 → 96 DPI），与历史口径一致。</para>
/// </summary>
internal sealed class LayoutBasisReader
{
    private readonly EntityManager _entityManager;
    private readonly ContainerBasisResolver? _basisResolver;
    private readonly ControlPool? _controlPool;

    internal LayoutBasisReader(
        EntityManager entityManager,
        ContainerBasisResolver? basisResolver,
        ControlPool? controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _basisResolver = basisResolver;
        _controlPool = controlPool;
    }

    /// <summary>物理控件 DPI（真源）；取不到返回 0，由调用方回退父控件 / 96。</summary>
    internal int ResolveDeviceDpi(Entity entity)
    {
        var pool = _controlPool ?? AppServiceProvider.Resolve<ControlPool>();
        return pool?.GetControl(entity) is Control control && !control.IsDisposed
            ? control.DeviceDpi
            : 0;
    }

    internal bool TryGetParentContainerSize(Entity entity,
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
}

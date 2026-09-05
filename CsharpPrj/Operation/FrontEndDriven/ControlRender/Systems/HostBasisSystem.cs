
#nullable enable

using System;
using System.Windows.Forms;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Container;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using Modules.LayoutAnalysis.UIInspector;

namespace FrontEndDriven.ECS.Systems;

/// <summary>
/// 宿主基准决议系统（M1·状态真源单向化）：scope 布局基准 = 宿主客户区（0×0 回退父面板客户区），
/// 统一写入 root 实体的 <see cref="HostBasisComponent"/>，替代 RenderStageRunner.AlignLayoutBasisToHost 的渲染前校准补丁。
/// 幂等：基准已一致则短路；字面 Form 宿主保持配置基准；宿主与父面板均未就绪时保持配置基准。
/// 只写 ECS（HostBasisComponent），重算由调用方在返回 true（基准变更）时触发作用域感知重算
/// （RenderStageRunner.ExecuteRenderStage → LayoutSystem.RecalculateScope(scope)，scope 为空时回退全量 RecalculateAll）。
/// </summary>
public sealed class HostBasisSystem
{
    private readonly EntityManager _entityManager;

    public HostBasisSystem(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    /// <summary>
    /// 决议 scope 的 root 实体宿主基准。
    /// </summary>
    /// <returns>基准是否发生变更（true 时调用方应触发布局重算）。</returns>
    public bool ResolveBasis(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return false;

        var hostMount = AppServiceProvider.Resolve<HostMountService>();
        var host = hostMount?.ResolveHost(scope);
        if (host == null)
        {
            // Step2：放开 Form 宿主——窗体宿主同样以客户区为基准随缩放/DPI 反应式对齐
            //（原「host is Form → 保持配置基准」的跳过使 Form 直挂 scope 永不自动刷新）。
            FrontEndDrivenLog.Log("HostBasisSystem_ResolveBasis_skip",
                new { scope, reason = "host_not_resolved", host = "<null>" });
            return false;
        }

        // 目标尺寸优先取宿主客户区；宿主自身 0×0（未挂根前 dock=None，挂载自愈后 Dock=Fill 填满父面板）→ 回退父面板客户区。
        // 实证：sideContainer 在 Side 渲染收集前 hostClient=0x0，而父 mainSplitter_Panel2=342x817 有效。
        var (targetWidth, targetHeight, basisSource) = ResolveTargetSize(host);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            FrontEndDrivenLog.Log("HostBasisSystem_ResolveBasis_unavailable",
                new { scope, host = host.Name ?? "<unnamed>", reason = "host_and_parent_zero_client" });
            return false;
        }

        // M1·坐标基准修复：布局基准原点 = 宿主全局 form 相对位置（ToFormRelativeAccurate，SplitterPanel 感知）。
        // 嵌套 scope（宿主不在 form 原点，如 contentContainer 在 mainSplitter.Panel2）的 ComputedLayoutComponent 因而
        // 成为真正「全局 form 相对」，与 LayoutCoordinateResolver.ToParentRelative 的契约（LayoutCoordinateResolverTests 语义）一致，
        // 消除「宿主相对坐标 + 全局父位置相减」导致的根节点/子节点重排错位。
        var (basisX, basisY) = ResolveHostFormPosition(host);

        var root = FindRootEntity(scope);
        if (!root.IsValid)
        {
            FrontEndDrivenLog.Log("HostBasisSystem_ResolveBasis_root_not_found",
                new { scope, rootName = $"root:{scope}" });
            return false;
        }

        // 幂等短路：布局基准已等于目标尺寸与原点
        if (_entityManager.TryGetComponent<HostBasisComponent>(root, out var current)
            && current.Left == basisX
            && current.Top == basisY
            && current.Width == targetWidth
            && current.Height == targetHeight)
        {
            FrontEndDrivenLog.Log("HostBasisSystem_ResolveBasis_already_aligned",
                new { scope, host = host.Name ?? "<unnamed>", hostClient = $"{targetWidth}x{targetHeight}", basisSource });
            return false;
        }

        _entityManager.AddOrSetComponent(root, new HostBasisComponent
        {
            Left = basisX,
            Top = basisY,
            Width = targetWidth,
            Height = targetHeight,
            BasisSource = basisSource
        });

        FrontEndDrivenLog.Log("HostBasisSystem_ResolveBasis",
            new { scope, host = host.Name ?? "<unnamed>", hostClient = $"{targetWidth}x{targetHeight}", basis = $"({basisX},{basisY})", basisSource });

        return true;
    }

    private static (int X, int Y) ResolveHostFormPosition(Control host)
    {
        // ToFormRelativeAccurate：SplitterPanel 感知（SplitterPanel.Left/Top 恒 0，经 SplitterDistance 换算），
        // 宿主位于 splitter 面板内时仍能给出正确的 form 相对位置。
        var rect = LayoutCoordinateResolver.ToFormRelativeAccurate(host);
        return (rect.X, rect.Y);
    }

    private static (int Width, int Height, string Source) ResolveTargetSize(Control host)
    {
        if (host.ClientSize.Width > 0 && host.ClientSize.Height > 0)
            return (host.ClientSize.Width, host.ClientSize.Height, "host");

        if (host.Parent is { ClientSize.Width: > 0, ClientSize.Height: > 0 } parent)
            return (parent.ClientSize.Width, parent.ClientSize.Height, "parentPanel");

        return (0, 0, "none");
    }

    private Entity FindRootEntity(string scope)
    {
        // D1 收敛：按名查实体统一经 EntityQueryService（FindRoot = FindByName($"root:{scope}")），删除手写遍历副本
        return AppServiceProvider.Resolve<EntityQueryService>()?.FindRoot(scope) ?? Entity.Invalid;
    }

    /// <summary>
    /// 构造宿主几何快照（Step1 布局失效值差判定输入）：与 ResolveBasis 读取完全同源的
    /// 客户区尺寸 + form 相对原点 + DPI，杜绝失效判定与重排各读一套物理属性。
    /// 宿主未就绪（0×0 / 未挂载）→ 返回 null，调用方回退按「提醒即调度」语义处理（与旧行为一致）。
    /// </summary>
    public static GeometrySnapshot? TryBuildGeometrySnapshot(Control host)
    {
        if (host == null || host.IsDisposed || host.Disposing)
            return null;

        var (width, height, _) = ResolveTargetSize(host);
        if (width <= 0 || height <= 0)
            return null; // 宿主/父面板均 0×0 → 未就绪，暂不缓存，等后续再判

        var (ox, oy) = ResolveHostFormPosition(host);
        return new GeometrySnapshot
        {
            Width = width,
            Height = height,
            OriginX = ox,
            OriginY = oy,
            DeviceDpi = host.DeviceDpi,
        };
    }
}

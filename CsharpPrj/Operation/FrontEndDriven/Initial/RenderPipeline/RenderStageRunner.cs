#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using EntityLifecycleBus = ECS.Core.Lifecycle.EntityLifecycleBus;
using FrameworkAbstractions.ComponentRegistry;
using UniversalMiddleware.Container;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.ECS.Systems;
using FrontEndDriven.ECS.Creation;
using FrontEndDriven.WinForms;
using Modules.Pipeline;
using Ports;
using TConfig;

namespace FrontEndDriven.Pipeline;

public sealed class RenderStageRunner
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly EcsInputPort _inputPort;
    private readonly CsgEntryPoint _collector;
    private readonly WinFormsOutputPort _outputPort;
    private readonly EcsCapabilityApplier _capabilityApplier;
    private readonly LayoutSystem _layoutSystem;
    private readonly HostBasisSystem _hostBasisSystem;
    private readonly SplitContainerSystem _splitContainerSystem;
    private readonly StyleSystem _styleSystem;
    private readonly EventBindingSystem _eventBindingSystem;
    private readonly SplitterPanelRegistry _splitterPanelRegistry;

    private readonly Func<bool> _isDisposedCheck;

    // 阶段 D：手写四步序列收敛为 StagePipeline<RenderContext>
    // （Load/Creation/Collect/Apply/Bind 五 Stage；Part* 区间控制等价原 ExecuteCore/ExecuteRenderStage 拆分）
    private readonly Modules.Pipeline.IStagePipeline<RenderContext> _pipeline;
    private readonly RenderContext _sharedContext = new();

    public RenderStageRunner(
        EntityManager entityManager,
        ControlPool controlPool,
        EcsInputPort inputPort,
        CsgEntryPoint collector,
        WinFormsOutputPort outputPort,
        EcsCapabilityApplier capabilityApplier,
        LayoutSystem layoutSystem,
        HostBasisSystem hostBasisSystem,
        SplitContainerSystem splitContainerSystem,
        StyleSystem styleSystem,
        EventBindingSystem eventBindingSystem,
        SplitterPanelRegistry splitterPanelRegistry,
        Func<bool> isDisposedCheck)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _inputPort = inputPort ?? throw new ArgumentNullException(nameof(inputPort));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _outputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
        _capabilityApplier = capabilityApplier ?? throw new ArgumentNullException(nameof(capabilityApplier));
        _layoutSystem = layoutSystem ?? throw new ArgumentNullException(nameof(layoutSystem));
        _hostBasisSystem = hostBasisSystem ?? throw new ArgumentNullException(nameof(hostBasisSystem));
        _splitContainerSystem = splitContainerSystem ?? throw new ArgumentNullException(nameof(splitContainerSystem));
        _styleSystem = styleSystem ?? throw new ArgumentNullException(nameof(styleSystem));
        _eventBindingSystem = eventBindingSystem ?? throw new ArgumentNullException(nameof(eventBindingSystem));
        _splitterPanelRegistry = splitterPanelRegistry ?? throw new ArgumentNullException(nameof(splitterPanelRegistry));
        _isDisposedCheck = isDisposedCheck ?? throw new ArgumentNullException(nameof(isDisposedCheck));

        // 构造五 Stage 管线（复用同一 composition 装配的端口/系统，符合双渲染管线 scope 参数化）
        // S4.5：Apply 之后、Bind 之前插入 RenderMountStage —— root 控件由 Apply 创建注册，挂载在其后；
        // M4 挂载前移验证失败（root 未注册时挂载入待挂队列无后续补挂），保持原时相。
        _pipeline = new StagePipeline<RenderContext>("RenderPipeline")
            .Register(new RenderLoadStage(_inputPort))
            .Register(new RenderCreationStage(_capabilityApplier, _layoutSystem, _splitContainerSystem, _styleSystem))
            .Register(new RenderCollectStage(_collector))
            .Register(new RenderApplyStage(_outputPort))
            .Register(new RenderMountStage())
            .Register(new RenderVisibilityCheckStage(_entityManager, _controlPool))
            .Register(new RenderBindStage(_eventBindingSystem))
            .Register(new RenderValidationStage(_entityManager));
    }

    public RenderRunResult ExecuteCore(
        UIConfiguration config,
        string scope)
    {
        var sw = Stopwatch.StartNew();
        var ctx = CreateSharedContext();
        ctx.Config = config;
        ctx.Scope = scope;

        // 等价原 ExecuteCore：Load → Creation（PartLoad+PartCreation，PartRender=false）
        SetParts(ctx, load: true, creation: true, render: false);
        _pipeline.ExecuteAsync(ctx).GetAwaiter().GetResult();
        sw.Stop();

        return BuildResult(ctx, renderExecuted: false, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// 渲染区间（S3 作用域感知）。scope 为 null/空 时保持全量收集语义（兼容无参调用）；
    /// 显式 scope 时收集器按 ScopeComponent 过滤，仅渲染目标作用域（修复「Side 加载却全量重放」缺陷）。
    /// </summary>
    public RenderRunResult ExecuteRenderStage(string? scope = null)
    {
        var sw = Stopwatch.StartNew();
        var ctx = CreateSharedContext();
        if (!string.IsNullOrEmpty(scope))
        {
            ctx.Scope = scope;
            // M1·真源单向化：宿主驱动的布局基准决议（HostBasisSystem 统一写 root 的 HostBasisComponent）。
            // M4 挂载前移尝试（基准决议移入 RenderMountStage）因 root 控件依赖 Apply 创建而回滚，保持渲染前决议。
            // 布局重算须与 S3 作用域感知一致：scope 非空时用 RecalculateScope 限定本作用域，
            // 避免 RecalculateAll 全量清除并置脏其它 scope（如 MainLayout）的 ContainerSize，造成跨 scope 误伤。
            // Step3·注册式收敛：基准决议 + ECS 重算统一归口 LayoutSubsystem.RecalculateComposition
            // （仅 ECS 组合、不投影，投影延后到挂载 ReflowNow），渲染管线不再直接持有 HostBasisSystem/LayoutSystem 协调逻辑。
            var layoutSubsystem = AppServiceProvider.Resolve<FrontEndDriven.ControlRender.Layout.LayoutSubsystem>();
            if (layoutSubsystem != null)
            {
                layoutSubsystem.RecalculateComposition(scope);
            }
            else
            {
                // 兜底：未注册布局子系统时保持原语义（基准决议 → 仅 ECS 重算，不投影）。
                if (_hostBasisSystem.ResolveBasis(scope))
                {
                    _layoutSystem.RecalculateScope(scope);
                }
            }
        }
        SetParts(ctx, load: false, creation: false, render: true);
        _pipeline.ExecuteAsync(ctx).GetAwaiter().GetResult();

        // S3·挂载后 ECS_MISMATCH 审计：布局真源在挂载后（ReflowNow 重算 + 投影）才收敛，
        // 审计须于管线（含挂载）完成后执行——否则首渲时物理 Bounds 尚未由投影写入 → 误报 ECS_MISMATCH。
        if (ctx.ApplyResult != null)
        {
            ctx.ApplyResult.MismatchCount = _outputPort.RunMismatchAudit(ctx.Scope);
        }

        sw.Stop();

        return BuildResult(ctx, renderExecuted: true, sw.ElapsedMilliseconds);
    }

    public void DestroyScopeEntities(string scopeName)
    {
        if (string.IsNullOrEmpty(scopeName)) return;

        var toDestroy = new List<Entity>();
        var entities = _entityManager.GetEntities<ScopeComponent>();

        // 架构收口（方案3）：销毁前先快照本作用域实体的控件名集合，用于宿主权属判定——
        // 只有「控件真正属于本作用域实体」的容器才在该作用域销毁时置 Orphaned。
        // 对「跨作用域宿主」（如 sideContainer：注册 Scope=Side，但控件实体属 MainLayout 骨架）不置 Orphaned，
        // 避免 Side 内容清理误伤骨架宿主。宿主挂载事实由宿主注入（EcsGate.DoLoadAndRender → MarkMounted）驱动，
        // 不由内容销毁裁决。
        var ownedControlNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (_entityManager.TryGetComponent<ScopeComponent>(entity, out var scope)
                && scope.Scope == scopeName)
            {
                toDestroy.Add(entity);
                var controlName = _controlPool.GetControlName(entity);
                if (!string.IsNullOrEmpty(controlName))
                    ownedControlNames.Add(controlName);
            }
        }

        foreach (var entity in toDestroy)
        {
            _splitterPanelRegistry.Unregister(entity);

            _outputPort.UnsubscribeSplitterMoved(entity);

            _splitContainerSystem.RemoveSubscription(entity);
        }

        // Phase 1·反应式布局子系统：scope 销毁时退订宿主 Resize（布局尺寸源单一清理点，防泄漏/重复订阅）。
        AppServiceProvider.Resolve<FrontEndDriven.ControlRender.Layout.LayoutSubsystem>()?.RemoveScope(scopeName);

        // 阶段1：scope 级失效广播（EntityLifecycleBus 唯一通知源）——订阅者自清理：
        // ControlPool（物理移除 + Dispose 控件解除事件委托 + 批量 Unregister 映射）、
        // HostMountService（待挂队列）等（EntityTreeService 已改走 ControlPool 权威索引，无需缓存失效订阅）。
        // 广播发生在实体销毁前（scopeEntities 为销毁前快照），订阅者可安全经实体解析控件。
        var lifecycleBus = AppServiceProvider.Resolve<EntityLifecycleBus>();
        lifecycleBus?.InvalidateScope(scopeName, toDestroy);

        // root 面板控件（宿主注入路径，root 实体未入实体→控件映射时按名兜底）：
        // ControlPool 订阅者按实体清理不到 root 面板，此处按 root:{scope} 名兜底清理，
        // 避免旧 root 面板残留在宿主中与新 root 并存。
        if (_controlPool.GetControl($"root:{scopeName}") is { } rootControl)
        {
            rootControl.Parent?.Controls.Remove(rootControl);
            rootControl.Dispose();
        }

        foreach (var entity in toDestroy)
        {
            _entityManager.DestroyEntity(entity);
        }

        // S4：作用域销毁后回写容器挂载状态（MountState=Orphaned），终结「挂载可见 ≠ 可被断言寻址」的推断残留。
        // 宿主权属守卫：仅当该注册容器的控件属于本作用域实体集时才置 Orphaned；否则视为跨作用域宿主，跳过（保持其挂载状态）。
        // 2026-08-05 R5：挂载状态写唯一经 IContainerLifecycle 出口（StateCenter 收敛）；registry 仅作枚举源，不再直接写挂载状态。
        var lifecycle = AppServiceProvider.Resolve<Share.StateManagement.IContainerLifecycle>();
        var registry = AppServiceProvider.Resolve<FrontEndDriven.Container.ContainerRegistry>();
        if (lifecycle != null && registry != null)
        {
            foreach (var handle in registry.GetAll())
            {
                if (!string.Equals(handle.Id.ScopeId, scopeName, StringComparison.Ordinal)) continue;
                if (handle.Control?.Name != null && !ownedControlNames.Contains(handle.Control.Name)) continue;

                lifecycle.MarkMounted(handle.Id.ScopeId, mounted: false);
            }
        }
    }

    // ── 私有辅助 ──

    private RenderContext CreateSharedContext()
    {
        _sharedContext.Reset();
        return _sharedContext;
    }

    private static void SetParts(RenderContext ctx, bool load, bool creation, bool render)
    {
        ctx.PartLoad = load;
        ctx.PartCreation = creation;
        ctx.PartRender = render;
    }

    private RenderRunResult BuildResult(RenderContext ctx, bool renderExecuted, long elapsedMs)
    {
        var collectedCount = renderExecuted && ctx.Snapshot != null ? ctx.Snapshot.Controls.Count : 0;
        var appliedCount = renderExecuted ? _outputPort.LastAppliedNodeCount : 0;

        // P7：挂载统计来自 RenderApplyStage 回填的 ApplyResult（仅渲染区间有效）
        var apply = renderExecuted ? ctx.ApplyResult : null;

        // R5：宿主真实子控件数——由 RenderMountStage 在物理挂载完成后回填的唯一真值（GetDirectChildCount 同口径）；
        // 无宿主或非状态管理场景（未回填）回退 0。消除 BuildResult 挂载前预计算（恒 0）+ 调用方二次覆盖的双算。
        var hostActualChildCount = ctx.HostActualChildCount ?? 0;

        // S1：最小化永久诊断——打印真实挂载数，作为宿主空态断言（教训：静态分析无法定位同名 root 覆盖时序）。
        // C5：RootContainer 参数已删（仅日志用），日志以 scope 定位。
        // C6：仅"已回填且为 0"（RenderMountStage 挂载后实测宿主空）才升 Info 高亮（挂载断链异常）；
        //     未回填（null，如 Execute 非渲染区间/非状态管理场景跳过挂载）属合法空态，走 Debug 降噪。
        if (!string.IsNullOrWhiteSpace(ctx.Scope))
        {
            var msgId = (ctx.HostActualChildCount == null || hostActualChildCount > 0)
                ? "RenderStageRunner_ActualHostChildCount_normal"
                : "RenderStageRunner_ActualHostChildCount";

            FrontEndDrivenLog.Log(msgId,
                new { scope = ctx.Scope, childCount = hostActualChildCount });
        }

        return new RenderRunResult
        {
            Scope = ctx.Scope,
            RootEntity = ctx.RootEntity,
            CollectedCount = collectedCount,
            AppliedCount = appliedCount,
            ElapsedMs = elapsedMs,
            RenderExecuted = renderExecuted,
            MountedCount = apply?.RelationsApplied ?? 0,
            // M3 内容层物理核验挂载数：= 物理核验后成功挂载的关系数（RelationsApplied）。用于双条件判据。
            ContentMounted = apply?.RelationsApplied ?? 0,
            OrphanCount = apply?.RelationsSkipped ?? 0,
            ControlsSkippedCount = apply?.ControlsSkipped ?? 0,
            SkipReasons = apply?.SkipReasons,
            HostActualChildCount = hostActualChildCount
        };
    }
}
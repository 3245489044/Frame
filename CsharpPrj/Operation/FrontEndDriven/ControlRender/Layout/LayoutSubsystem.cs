#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Systems;
using FrameworkAbstractions.ComponentRegistry;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 反应式布局子系统：把「宿主几何变化 → 布局重排」从内容渲染管线中剥离为独立反应式回路。
/// 真源层级：布局意图(ECS, 不可变) ⊗ 基准尺寸(物理, 反应式输入 host.ClientSize) → 派生布局(幂等重算) → 物理投影(差异)。
/// 流程：宿主几何信号（Resize/Move 主回路 + 低频兜底扫描，Step2 展开）→ 值差门控（几何真变）→ LayoutScheduler 合并调度
///       → HostBasisSystem.ResolveBasis（基准变更才重算，幂等短路）
///       → LayoutSystem.RecalculateScope（scope 感知重算）→ LayoutProjector.ProjectScope（仅 Bounds 差异投影，不碰结构/属性/状态）。
/// 生命周期单收口：RegisterScope（挂载）/ RemoveScope（销毁）。
/// 防反馈：_projecting 重入守卫——投影自身 SetBounds 触发的宿主几何事件不再入队（内容投影与布局投影互不因对方再次触发）。
/// </summary>
public sealed class LayoutSubsystem : IDisposable
{
    private readonly Func<HostMountService?> _hostMountResolver;

    // 测试态宿主解析缝：非空时优先于 HostMountService（单测无法构造完整容器注册表）。
    private readonly Func<string, Control?>? _hostResolver;

    // 测试态就绪判定缝：非空时替代真实宿主尺寸分类（WinForms 无法稳定构造 0×0 客户区）。
    private readonly Func<string, LayoutHostReadiness>? _readinessResolver;

    // 退避时间源（可注入：单测用可控时钟验证真实退避窗口，避免真实 sleep）。
    private readonly Func<long> _clock;

    private LayoutScheduler? _scheduler;

    // 重流依赖：生产路径由 Create 注入；测试态（仅验证订阅/调度接线）可为空，重流方法短路返回。
    private readonly HostBasisSystem? _hostBasisSystem;
    private readonly LayoutSystem? _layoutSystem;
    private readonly LayoutProjector? _projector;

    private sealed record Tracked(Control Host, EventHandler Handler)
    {
        /// <summary>退订 Host 的全部几何失效信号（Resize/Move 共用 Handler）。</summary>
        public void Unsubscribe()
        {
            try
            {
                Host.Resize -= Handler;
                Host.Move -= Handler;
            }
            catch { /* 控件已释放，退订可忽略 */ }
        }
    }

    private readonly ConcurrentDictionary<string, Tracked> _tracked = new(StringComparer.OrdinalIgnoreCase);

    // Step1·值差门控：上一次判定后的几何快照缓存。Resize 只作「提醒」，几何真变才调度重排。
    // 仅生产路径（hostBasisSystem 注入）使用；测试态（无重流依赖）回退「提醒即调度」保接线原义。
    private readonly ConcurrentDictionary<string, GeometrySnapshot> _lastGeometry = new(StringComparer.OrdinalIgnoreCase);

    // 确定性收敛状态机：每 scope 的收敛状态、退避次数与下一次允许重试时间（UI 单消费者，无锁访问）。
    private sealed record ConvergenceTrack(LayoutConvergenceState State, int RetryAttempt, long NextRetryAtMs);

    private readonly ConcurrentDictionary<string, ConvergenceTrack> _convergence = new(StringComparer.OrdinalIgnoreCase);

    private int _projecting;

    // Step2·兜底网：低频定时扫描已注册 scope 的几何快照，值差真变才调度——
    // 兜住「主回路（Resize/Move）未收到信号」的边界（如仅 DPI 变而尺寸不变、个别宿主静默改几何）。
    // 仅生产路径创建；测试态（内部 ctor）不建定时器，保持订阅/调度接线纯同步。
    private System.Windows.Forms.Timer? _sweepTimer;

    private bool _disposed;

    /// <summary>
    /// 生产工厂：校验全部依赖并装配调度器（调度器工作委托引用本实例重流方法，规避构造环）。
    /// </summary>
    public static LayoutSubsystem Create(
        Func<TConfig.Abstractions.IUIDispatcher?> dispatcherResolver,
        Func<HostMountService?> hostMountResolver,
        HostBasisSystem hostBasisSystem,
        LayoutSystem layoutSystem,
        LayoutProjector projector)
    {
        if (dispatcherResolver == null) throw new ArgumentNullException(nameof(dispatcherResolver));
        if (hostMountResolver == null) throw new ArgumentNullException(nameof(hostMountResolver));
        ArgumentNullException.ThrowIfNull(hostBasisSystem);
        ArgumentNullException.ThrowIfNull(layoutSystem);
        ArgumentNullException.ThrowIfNull(projector);

        var instance = new LayoutSubsystem(hostMountResolver, hostBasisSystem, layoutSystem, projector);
        instance._scheduler = new LayoutScheduler(dispatcherResolver, instance.Reflow, instance.ShouldRetry, instance.Quarantine);
        return instance;
    }

    /// <summary>测试态构造（仅订阅/调度接线；重流依赖为空 → 重流方法短路）。</summary>
    internal LayoutSubsystem(LayoutScheduler scheduler, Func<HostMountService?> hostMountResolver)
        : this(scheduler, hostMountResolver, null, null, null)
    {
    }

    /// <summary>
    /// 测试态构造（可注入宿主解析 / 时钟 / 就绪判定）：验证退避时间窗、预算复位与 Abandoned 自愈。
    /// </summary>
    internal LayoutSubsystem(
        LayoutScheduler scheduler,
        Func<HostMountService?> hostMountResolver,
        Func<string, Control?>? hostResolver,
        Func<long>? clock,
        Func<string, LayoutHostReadiness>? readinessResolver = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _hostMountResolver = hostMountResolver ?? throw new ArgumentNullException(nameof(hostMountResolver));
        _hostResolver = hostResolver;
        _readinessResolver = readinessResolver;
        _clock = clock ?? (() => Environment.TickCount64);
        _hostBasisSystem = null;
        _layoutSystem = null;
        _projector = null;
    }

    private LayoutSubsystem(
        Func<HostMountService?> hostMountResolver,
        HostBasisSystem hostBasisSystem,
        LayoutSystem layoutSystem,
        LayoutProjector projector)
    {
        _hostMountResolver = hostMountResolver ?? throw new ArgumentNullException(nameof(hostMountResolver));
        _hostResolver = null;
        _readinessResolver = null;
        _clock = () => Environment.TickCount64;
        _hostBasisSystem = hostBasisSystem;
        _layoutSystem = layoutSystem;
        _projector = projector;

        // 装配兜底扫描定时器（UI 线程低频，Tick → 全量值差扫描，真变才调度重排）。
        // WinForms Timer 自行挂后台隐藏窗口，随消息泵触发；几何未变时零调度、零日志（quiet）。
        _sweepTimer = new System.Windows.Forms.Timer
        {
            Interval = LayoutConvergencePolicy.GeometrySweepIntervalMs
        };
        _sweepTimer.Tick += (_, _) => SweepHostGeometries();
        _sweepTimer.Start();
    }

    /// <summary>
    /// 注册 scope 宿主并订阅其几何失效信号（幂等：同 scope 同 host 短路）。
    /// Step2·失效源：宿主统一订阅 Resize/Move 两路几何信号（主回路） + 低频兜底扫描（Step2 实现），
    /// 全部经 OnHostGeometryChange 的「值差门控」过滤——几何真变才调度重排。
    /// 宿主为 Form 不再跳过：窗体宿主同样随缩放/移动/DPI 反应式对齐（根除 Form 直挂 scope 不自动刷新）。
    /// </summary>
    public void RegisterScope(string scope, Control host)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(scope) || host == null) return;

        // Tab 可达成区就绪触发（TabControl.Resize → 该 scope reflow）——独立于宿主类型。
        // 布局基准真源绑定到承载几何的控件自身尺寸事件（布局引擎更新后才触发，DisplayRectangle 已就绪，
        // ContainerBasisResolver 在 reflow 内同步读偏移量推导），消除「缓存组件回写 + 二次异步排空」读滞后一拍。
        AppServiceProvider.Resolve<TabContentReflowSystem>()?.Attach(scope);

        if (_tracked.TryGetValue(scope, out var existing))
        {
            if (ReferenceEquals(existing.Host, host)) return; // 幂等短路
            existing.Unsubscribe();
            _tracked.TryRemove(scope, out _);
        }

        // Step2·失效源：同一回调挂接两路几何信号（Resize/Move），供值差门控判定。
        // DPI 变化会同步触发宿主 Resize（客户区随缩放比例变化），故不另订阅当前环境缺失的 DpiChanged；
        // DeviceDpi 仍入快照参与值差，兜底扫描会补齐「仅 DPI 变而尺寸不变」的边界。
        EventHandler handler = new((_, _) => OnHostGeometryChange(scope, host));
        host.Resize += handler;
        host.Move += handler;
        _tracked[scope] = new Tracked(host, handler);

        // 挂载就绪信号（RenderMountStage 在挂载完成后调用本方法）：重置该 scope 的收敛状态，
        // 使此前 Blocked/Abandoned 的历史不阻碍新挂载的首布局（随后 ReflowNow 单次触发）。
        _convergence.TryRemove(scope, out _);
        _lastGeometry.TryRemove(scope, out _); // 重置几何快照缓存，避免跨挂载复用旧基准判定

        FrontEndDrivenLog.Log("LayoutSubsystem_tracked", new { scope, host = host.Name ?? "<unnamed>" });
    }

    /// <summary>移除 scope：退订全部几何失效信号 + 托管几何真源 + 清跟踪（销毁路径单一收口）。</summary>
    public void RemoveScope(string scope)
    {
        if (_tracked.TryRemove(scope, out var tracked))
        {
            tracked.Unsubscribe();
            FrontEndDrivenLog.Log("LayoutSubsystem_untracked", new { scope });
        }

        // 销毁路径一并清理收敛状态与几何快照缓存，避免 scope 残留 Blocked/Abandoned / 旧基准污染重载。
        _convergence.TryRemove(scope, out _);
        _lastGeometry.TryRemove(scope, out _);

        AppServiceProvider.Resolve<TabContentReflowSystem>()?.Detach(scope);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止并释放兜底扫描定时器（避免消息泵继续触发扫描）。
        _sweepTimer?.Stop();
        _sweepTimer?.Dispose();
        _sweepTimer = null;

        foreach (var scope in _tracked.Keys.ToArray())
            RemoveScope(scope);
        _tracked.Clear();
        _convergence.Clear();
        _lastGeometry.Clear();
    }

    /// <summary>
    /// Step2·兜底扫描：低频（定时器 Tick）全量扫描已注册 scope，复用 OnHostGeometryChange 的
    /// 「值差门控」判定——几何真变才 Schedule，无变则以 quiet 静默返回（零日志、零调度）。
    /// 兜住主回路（Resize/Move）可能遗漏的宿主几何变化边界（如仅 DPI 变而尺寸不变）。
    /// </summary>
    private void SweepHostGeometries()
    {
        if (_disposed) return;

        foreach (var kvp in _tracked)
        {
            var (scope, tracked) = (kvp.Key, kvp.Value);
            if (tracked.Host.IsDisposed || tracked.Host.Disposing)
            {
                RemoveScope(scope);
                continue;
            }
            OnHostGeometryChange(scope, tracked.Host, quiet: true);
        }
    }

    private void OnHostGeometryChange(string scope, Control host, bool quiet = false)
    {
        if (_disposed) return;

        if (host.IsDisposed || host.Disposing)
        {
            RemoveScope(scope);
            return;
        }

        // 终态隔离的 scope 不再因「未就绪」信号空转；但有效几何变化必须能自愈（见下方 ReviveFromAbandoned）。
        bool abandoned = IsAbandoned(scope);

        // 防反馈：投影执行中（布局/内容投影 SetBounds 可能触发宿主几何事件），不再入队，避免自触发循环。
        if (_projecting != 0)
            return;

        // Step1/2·值差门控：几何信号（Resize/Move/兜底扫描）只作「提醒」，「几何真变」才调度重排。
        // 未就绪（快照无效）→ 不调度、不消耗退避预算：1Hz 兜底扫描不再把预算烧穿，
        // 等宿主真正就绪后的首个有效几何信号再重排。
        // 测试态（hostBasisSystem=null）→ 以宿主/父面板客户区可读作为有效信号。
        if (_hostBasisSystem != null)
        {
            var snapshot = HostBasisSystem.TryBuildGeometrySnapshot(host);
            if (snapshot is not { IsValid: true } current)
                return;

            var last = _lastGeometry.TryGetValue(scope, out var prev) ? prev : default;
            _lastGeometry[scope] = current; // 刷新缓存，供下次差集判定
            if (!current.Changed(in last))
            {
                // 主回路（非 quiet）记录未变化；兜底扫描（quiet）静默返回，避免 1Hz 空转日志刷屏。
                if (!quiet)
                    FrontEndDrivenLog.Log("LayoutSubsystem_geometry_unchanged",
                        new { scope, geometry = current.ToString() });
                return; // 几何未变 → 不调度
            }

            if (abandoned)
                ReviveFromAbandoned(scope, "host-geometry-changed");
        }
        else if (abandoned)
        {
            if (ClassifyReadiness(scope) != LayoutHostReadiness.Ready)
                return;
            ReviveFromAbandoned(scope, "host-geometry-changed");
        }

        _scheduler?.Schedule(scope);
    }

    /// <summary>
    /// R3·统一重排入口（Splitter 真源闭环 / 其它布局输入）：经 LayoutScheduler 入队，
    /// 与宿主 Resize 重排共用同一 latest-wins 调度、UI 线程单消费者串行执行，消除多源并发踩踏与重复投影。
    /// 结果与宿主 Resize 一致：无条件 Reflow（splitter 所属 scope 的宿主客户区未必变化，故不可用 ResolveBasis 门短路）。
    /// </summary>
    public void RequestReflow(string scope)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (IsAbandoned(scope) && !TryReviveIfHostReady(scope, "request-reflow")) return;

        _scheduler?.Schedule(scope);
    }

    /// <summary>
    /// R3·同步重排（首渲挂载后一次性调用）：与宿主 Resize 共用同一 Reflow 路径
    /// （ResolveBasis → RecalculateScope → Project，纯 ECS 坐标 + 物理基线差异投影）。
    /// 在「宿主已挂载 root、真实客户区就绪」时同步执行，使首次加载即由 ECS 布局真源写入正确 Bounds，
    /// 消除「内容 Apply 在 root 未挂宿主时用物理父链换算→多重偏移，直到拉伸才纠正」的挂载时相缺陷。
    /// 仅重流依赖（HostBasisSystem/LayoutSystem/LayoutProjector）齐备时执行；测试态短路返回。
    /// </summary>
    public void ReflowNow(string scope)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (IsAbandoned(scope) && !TryReviveIfHostReady(scope, "reflow-now")) return;

        Reflow(scope);
    }

    /// <summary>
    /// Step3·注册式收敛：渲染管线「仅 ECS 组合、暂不物理投影」阶段的统一入口。
    /// 与 Reflow 的区别：本方法只做「基准决议(ResolveBasis) + 作用域 ECS 重算(RecalculateScope)」，
    /// 不触发物理投影（Bounds 由挂载路径 ReflowNow 统一写入，避免渲染组合阶段双重投影）。
    /// 归口本子系统后，渲染管线不再直接依赖 HostBasisSystem/LayoutSystem，基准决议→重算协调集中到单一仲裁者。
    /// 仅重流依赖齐备时执行（生产路径）；测试态/未注册子系统短路，由调用方回退旧语义。
    /// </summary>
    public void RecalculateComposition(string scope)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (IsAbandoned(scope) && !TryReviveIfHostReady(scope, "recalculate-composition")) return;
        if (_hostBasisSystem == null || _layoutSystem == null) return; // 测试态短路

        if (_hostBasisSystem.ResolveBasis(scope))
            _layoutSystem.RecalculateScope(scope);
    }

    /// <summary>
    /// 统一重排工作（LayoutScheduler 单消费者执行）：宿主就绪门控 → ResolveBasis（更新基准，幂等）
    /// → RecalculateScope（scope 感知重算）→ Project（仅 Bounds 差异投影）。
    /// 宿主 Resize 与 splitter 拖拽共用本方法——一律重算，
    /// 由 ProjectScope 的物理基线差异保证「无变化即零写入」（重算成本 scope 局部、可接受）。
    /// 确定性收敛状态机门控（治「未就绪反复空转」）：
    ///   Blocked（宿主未挂载/Form）→ 不重排、不入队、不空转，等待挂载信号；
    ///   Retrying（宿主已挂载但基准瞬态 0x0）→ 真实时间窗退避（1s/2s/4s），窗口内 Deferred 不耗预算；
    ///   Abandoned（退避预算耗尽）→ 明确放弃并告警；有效几何变化/重新挂载/有效重排请求可自愈。
    /// </summary>
    private void Reflow(string scope)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (IsAbandoned(scope)) return; // 终态隔离兜底：即便被直接调用也不重排

        // ① 宿主就绪门控（不依赖重流依赖，测试态也执行，短路后无更多动作）
        var readiness = ClassifyReadiness(scope);
        switch (readiness)
        {
            case LayoutHostReadiness.Blocked:
                SetConvergenceState(scope, LayoutConvergenceState.Blocked);
                FrontEndDrivenLog.Log("LayoutSubsystem_convergence_blocked",
                    new { scope, note = "宿主未挂载/Form，等待挂载信号，不重排不空转" });
                return;
            case LayoutHostReadiness.Retrying:
                switch (TryAdvanceRetry(scope))
                {
                    case RetryAdvance.Deferred:
                        return; // 退避窗口内：静默等待，不重排、不消耗预算
                    case RetryAdvance.Exhausted:
                        // 退避预算耗尽 → 统一 Abandon 入口（budgetKind=reflow；不误退订宿主）
                        Abandon(scope, "宿主/基准连续未就绪，退避预算耗尽，明确放弃（有效几何或重新挂载可自愈）",
                            budgetKind: "reflow", detach: false);
                        return;
                    default:
                        SetConvergenceState(scope, LayoutConvergenceState.Retrying);
                        FrontEndDrivenLog.Log("LayoutSubsystem_convergence_retrying",
                            new { scope, attempt = RetryAttemptOf(scope) });
                        break;
                }

                break;
        }

        if (_hostBasisSystem == null || _layoutSystem == null || _projector == null)
        {
            // 测试态/未注入重流依赖：仅「已就绪」可视为收敛（复位退避预算）；
            // Retrying 已在上方推进退避，不得被覆写为 Converged。
            if (readiness == LayoutHostReadiness.Ready)
                SetConvergenceState(scope, LayoutConvergenceState.Converged);
            return;
        }

        _hostBasisSystem.ResolveBasis(scope); // 更新基准（若宿主变化则同步，幂等）

        // Tab 可达成显示区由 ContainerBasisResolver 在本 reflow 内从物理 DisplayRectangle 偏移量同步推导：
        // 本处不预读缓存。宿主 Resize 时嵌套 TabControl 尚未被 WinForms 布局引擎重新布局，
        // 但 TabPage 基准的尺寸取同步父区（ContainerSizeComponent）、仅 inset 取偏移量（相对稳定），
        // 且 TabControl 自身 Resize 会再触发一次 reflow 自愈——不会再读滞后旧值覆盖正确基准。
        _layoutSystem.RecalculateScope(scope);

        int updated = Project(scope);

        // Pillar B·settle 兜底：投影后立即把几何快照缓存刷新到"投影后"值。任何在 `_projecting` 归零后才到达
        // 的尾随自写回几何事件（异步/消息泵延迟路径），其快照与本值一致 → 值差门控判"未变化" → 不再自触发重排。
        RefreshPostReflowGeometry(scope);

        // ③ 幂等短路：本轮投影无实际变更 → 已收敛（收敛状态落定，不再产生调度）
        SetConvergenceState(scope, LayoutConvergenceState.Converged);
        if (updated == 0)
            FrontEndDrivenLog.Log("LayoutSubsystem_reflow_converged", new { scope });
        else
            FrontEndDrivenLog.Log("LayoutSubsystem_reflow", new { scope, updated });
    }

    /// <summary>Pillar B：把 scope 宿主几何快照缓存刷新到当前（投影后）值，供尾随自写回事件判"未变化"。</summary>
    private void RefreshPostReflowGeometry(string scope)
    {
        var host = ResolveHostControl(scope);
        if (host == null || host.IsDisposed || host.Disposing)
            return;

        var snapshot = HostBasisSystem.TryBuildGeometrySnapshot(host);
        if (snapshot is { IsValid: true } s)
            _lastGeometry[scope] = s;
    }

    /// <summary>宿主就绪分类（门控输入，LayoutConvergencePolicy.ClassifyHost 纯函数接线）。</summary>
    private LayoutHostReadiness ClassifyReadiness(string scope)
    {
        if (_readinessResolver != null)
            return _readinessResolver(scope);

        // 就绪判定单一实现（F-07）：尺寸/父面板回退规则只在 HostBasisSystem，本层不再复制。
        return HostBasisSystem.ClassifyHost(ResolveHostControl(scope));
    }

    /// <summary>
    /// 置 scope 收敛状态（幂等）。Converged/Blocked 视作「本轮事件结束」——复位退避计数，
    /// 使下一次独立未就绪事件拥有完整预算（避免历史失败污染后续瞬态）。
    /// </summary>
    private void SetConvergenceState(string scope, LayoutConvergenceState state)
    {
        var track = _convergence.GetOrAdd(scope, _ => new ConvergenceTrack(state, 0, 0));
        bool resetBudget = state is LayoutConvergenceState.Converged or LayoutConvergenceState.Blocked;
        if (track.State == state && (!resetBudget || track.RetryAttempt == 0))
            return;
        _convergence[scope] = track with
        {
            State = state,
            RetryAttempt = resetBudget ? 0 : track.RetryAttempt,
            NextRetryAtMs = resetBudget ? 0 : track.NextRetryAtMs
        };
    }

    /// <summary>退避推进结果：已推进 / 退避窗口内等待 / 预算耗尽。</summary>
    private enum RetryAdvance
    {
        Advanced,
        Deferred,
        Exhausted
    }

    /// <summary>
    /// 推进 Retrying 退避一次：按 <see cref="LayoutConvergencePolicy.NextBackoff"/> 的真实时间窗推进，
    /// 窗口内重复调用一律 Deferred（不重排、不耗预算）；超出序列长度 → Exhausted（→ Abandoned）。
    /// </summary>
    private RetryAdvance TryAdvanceRetry(string scope)
    {
        var track = _convergence.GetOrAdd(scope, _ => new ConvergenceTrack(LayoutConvergenceState.Retrying, 0, 0));
        long now = _clock();
        if (now < track.NextRetryAtMs)
            return RetryAdvance.Deferred;

        // 唯一终止判据（F-12）：ShouldAbandon；NextBackoff 仅提供延时窗口。
        if (LayoutConvergencePolicy.ShouldAbandon(track.RetryAttempt))
            return RetryAdvance.Exhausted;

        var backoffSeconds = LayoutConvergencePolicy.NextBackoff(track.RetryAttempt);
        if (backoffSeconds == null)
            return RetryAdvance.Exhausted;

        _convergence[scope] = track with
        {
            State = LayoutConvergenceState.Retrying,
            RetryAttempt = track.RetryAttempt + 1,
            NextRetryAtMs = now + backoffSeconds.Value * 1000L
        };
        return RetryAdvance.Advanced;
    }

    private int RetryAttemptOf(string scope)
        => _convergence.TryGetValue(scope, out var t) ? t.RetryAttempt : 0;

    /// <summary>解析 scope 宿主控件（生产：HostMountService；测试：注入解析器优先）。</summary>
    private Control? ResolveHostControl(string scope)
        => _hostResolver?.Invoke(scope) ?? _hostMountResolver()?.ResolveHost(scope);

    /// <summary>
    /// 已放弃 scope 的自愈判定：宿主已解析且客户区/父面板可读 → 解除 Abandoned 隔离并允许重新入队。
    /// 生产路径额外要求几何快照有效，杜绝 0×0 期间再次空转；
    /// 若 scope 已被 Quarantine 退订，则重新 RegisterScope 恢复订阅。
    /// </summary>
    private bool TryReviveIfHostReady(string scope, string reason)
    {
        var host = ResolveHostControl(scope);
        if (host == null) return false;

        bool ready = _hostBasisSystem != null
            ? HostBasisSystem.TryBuildGeometrySnapshot(host) is { IsValid: true }
            : ClassifyReadiness(scope) == LayoutHostReadiness.Ready;
        if (!ready) return false;

        if (!_tracked.ContainsKey(scope))
            RegisterScope(scope, host); // 恢复几何订阅（Quarantine 曾退订）
        ReviveFromAbandoned(scope, reason);
        return true;
    }

    /// <summary>解除 Abandoned 隔离（复位为未跟踪=Converged 默认），并留可见证据。</summary>
    private void ReviveFromAbandoned(string scope, string reason)
    {
        _convergence.TryRemove(scope, out _);
        FrontEndDrivenLog.Log("LayoutSubsystem_convergence_revived", new { scope, reason });
    }

    /// <summary>放弃诊断：把「为什么判定未就绪」变成结构事实（宿主/客户区/父客户区/attempt）。</summary>
    private object BuildAbandonDiagnostics(string scope, string note, string budgetKind)
    {
        var host = ResolveHostControl(scope);
        var parent = host?.Parent;
        return new
        {
            scope,
            note,
            budgetKind,
            attempt = RetryAttemptOf(scope),
            host = host?.Name ?? "<null>",
            hostClient = host == null ? "<null>" : $"{host.ClientSize.Width}x{host.ClientSize.Height}",
            parentClient = parent == null ? "<null>" : $"{parent.ClientSize.Width}x{parent.ClientSize.Height}"
        };
    }

    /// <summary>
    /// 仅 Bounds 差异投影（重入守卫包裹），返回变更实体数。
    /// 批量 SetBounds 以宿主 SuspendLayout/ResumeLayout(true) 包裹——抑制投影自身 SetBounds
    /// 触发的宿主/子容器 Resize 自触发（治「Reflow → Project → Resize → 再 Reflow」反馈回路），
    /// ResumeLayout(true) 使挂起布局一次性到位。
    /// Pillar B·settle 窗口：`_projecting` 从 SuspendLayout 前起、到 ResumeLayout(true) 执行完毕止，
    /// 使延迟布局引擎（Dock/Anchor 二次调整）同步触发的 Resize/Move 几何事件也处于守卫内而被抑制入队——
    /// 消除「延迟布局在 _projecting 归 0 后才执行 → 自写回事件逃逸守卫 → 重新入队」的振荡通道。
    /// </summary>
    private int Project(string scope)
    {
        if (_projector == null)
            return 0;

        var host = ResolveHostControl(scope);
        bool suspended = host != null && !host.IsDisposed && !host.Disposing;

        Interlocked.Increment(ref _projecting);
        try
        {
            if (suspended)
                host!.SuspendLayout();

            int changed = _projector.ProjectScope(scope);

            if (suspended)
                host!.ResumeLayout(true); // true = 立即执行挂起的布局（一次性到位）；仍处于 _projecting → 事件被抑制

            return changed;
        }
        finally
        {
            Interlocked.Decrement(ref _projecting);
        }
    }

    /// <summary>诊断：scope 当前收敛状态（审计/单测用；未跟踪视为已收敛）。</summary>
    internal LayoutConvergenceState GetConvergenceState(string scope)
        => _convergence.TryGetValue(scope, out var t) ? t.State : LayoutConvergenceState.Converged;

    /// <summary>
    /// 预算耗尽重排判定（LayoutScheduler 注入）：仅 Retrying（瞬态未就绪，退避内）可重试；
    /// Blocked（等待挂载）/Converged（已收敛）/Abandoned（已放弃）不重排，杜绝不可收敛 scope 无限自激。
    /// 未跟踪 scope 视为可重试（默认 true），避免预算耗尽后 scope 静默滞留（F6 修复目标不回退）。
    /// </summary>
    internal bool ShouldRetry(string scope)
        => !_convergence.TryGetValue(scope, out var t) || t.State == LayoutConvergenceState.Retrying;

    /// <summary>scope 是否处于终态隔离（Abandoned）。</summary>
    private bool IsAbandoned(string scope)
        => _convergence.TryGetValue(scope, out var t) && t.State == LayoutConvergenceState.Abandoned;

    /// <summary>
    /// 终态隔离（Quarantine）：由 LayoutScheduler 预算耗尽超限触发（per-scope 判定，仅影响该 scope）。
    /// 退订 scope 宿主几何信号 + 拆分粒子系统，并把收敛状态置 Abandoned——
    /// 使后续 <see cref="OnHostGeometryChange"/> / <see cref="RequestReflow"/> / <see cref="ReflowNow"/> /
    /// <see cref="RecalculateComposition"/> / <see cref="Reflow"/> 全部短路（不再入队/重排），
    /// 从触发源上掐断「无法物理收敛 scope」跨 drain 反复重新入队的空转；
    /// 保留 Abandoned 标记（区别于 <see cref="RemoveScope"/> 的清缓存），等待重新挂载
    /// （<see cref="RegisterScope"/>）或有效重排请求（<see cref="RequestReflow"/>/<see cref="ReflowNow"/>）自愈。
    /// </summary>
    private void Quarantine(string scope)
        => Abandon(scope, "调度器预算耗尽（连续重排未物理收敛），终态隔离，等待挂载/有效重排请求自愈",
            budgetKind: "drain", detach: true);

    /// <summary>
    /// 统一放弃入口（F-08）：两类预算只在 <paramref name="budgetKind"/> 上区分，
    /// 是否退订宿主由 <paramref name="detach"/> 显式声明（drain 隔离退订，reflow 耗尽不退订）。
    /// </summary>
    private void Abandon(string scope, string reason, string budgetKind, bool detach)
    {
        if (_disposed || string.IsNullOrWhiteSpace(scope)) return;
        if (IsAbandoned(scope)) return; // 幂等：同一 scope 只落一次放弃

        if (detach)
        {
            if (_tracked.TryRemove(scope, out var tracked))
                tracked.Unsubscribe();
            AppServiceProvider.Resolve<TabContentReflowSystem>()?.Detach(scope);
        }

        SetConvergenceState(scope, LayoutConvergenceState.Abandoned);
        FrontEndDrivenLog.Log("LayoutSubsystem_convergence_abandoned",
            BuildAbandonDiagnostics(scope, reason, budgetKind));
    }
}

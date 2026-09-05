#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ECS.Components;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Eventing;
using FrontEndDriven.Pipeline;
using Ports;
using Share.StateManagement;
using TConfig;

namespace FrontEndDriven.Gate;

public sealed class EcsGate : IDisposable
{
    private readonly RenderPipelineComposition _composition;
    private readonly List<IEcsInterceptor> _interceptors = new();
    private bool _disposed;

    private EventPlatform? _eventPlatform;

    // 2026-08-05 StateCenter 收敛（R3）：scope 销毁统一经 ICompositionLifecycle 出口。
    // M6（2026-08-06）：挂载状态写入已从本类移除（MarkMounted 单写 = ScopeLifecycleAdapter.RenderAsync ⑥），
    // IContainerLifecycle 字段与 SetContainerLifecycle 已删除。
    private ICompositionLifecycle? _compositionLifecycle;

    /// <summary>最近一次渲染区间结果快照（E7：GetLastRenderStats 读此快照，替代穿透 Composition 旧计数）。</summary>
    private RenderRunResult? _lastRenderResult;

    public EcsGate(RenderPipelineComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _interceptors.Add(new AuditLogInterceptor());
    }

    public RenderPipelineComposition Composition => _composition;

    public void SetEventPlatform(EventPlatform? platform) => _eventPlatform = platform;

    /// <summary>2026-08-05 StateCenter 收敛（R3）：scope 销毁出口（绝不 Dispose composition）。</summary>
    public void SetCompositionLifecycle(ICompositionLifecycle? lifecycle) => _compositionLifecycle = lifecycle;

    public void AddInterceptor(IEcsInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _interceptors.Add(interceptor);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        EcsOperation<TResult> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_disposed) throw new ObjectDisposedException(nameof(EcsGate));

        // E4：OnBefore 循环移入 try 内——拦截器抛异常时错误路径完整（catch 补调 OnError）
        try
        {
            foreach (var i in _interceptors) await i.OnBefore(operation, ct);
            var result = await DispatchAsync(operation, ct);
            foreach (var i in _interceptors) await i.OnAfter(operation, result, ct);
            return result;
        }
        catch (Exception ex)
        {
            foreach (var i in _interceptors) await i.OnError(operation, ex, ct);
            throw;
        }
    }

    public async Task<RenderRunResult> LoadConfigAndRenderAsync(
        UIConfiguration config,
        string scope,
        System.Windows.Forms.Control? hostControl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("作用域名称不能为空", nameof(scope));

        // P3/RD2：合并为单一原子操作，消除「Dispatch(LoadConfig) + 独立 ExecuteRenderStage」双轨
        // P7：返回 RenderRunResult（含挂载统计）——「渲染执行 ≠ 挂载可见」由 Succeeded 判据暴露
        return await ExecuteAsync(new LoadAndRenderOperation
        {
            Config = config,
            Scope = scope,
            HostControl = hostControl
        }, ct).ConfigureAwait(false);
    }

    // ── 只读门面（聚拢点收口）：宿主经此查询渲染统计与控件，禁止穿透 .Composition / .ControlPool ──

    /// <summary>最近一次渲染统计（宿主导航模板读取用，不暴露 Composition 内部）。</summary>
    public RenderStats GetLastRenderStats()
    {
        ThrowIfDisposed();

        // E7：优先读最近一次渲染区间结果快照（DoLoadAndRender 落定），语义=「本次 boot 最后一次渲染」；
        // 未执行过渲染时回退 Composition 计数。
        var last = _lastRenderResult;
        var collected = last?.CollectedCount ?? _composition.LastCollectedNodeCount;
        var applied = last?.AppliedCount ?? _composition.OutputPort?.LastAppliedNodeCount ?? 0;
        var ratio = collected > 0 ? (double)applied / collected : 1.0;

        return new RenderStats(
            CollectedCount: collected,
            AppliedCount: applied,
            AppliedRatio: ratio,
            HasExecuted: last?.RenderExecuted ?? _composition.HasExecuted);
    }

    /// <summary>按名查找普通控件（ControlPool 索引收口），替代外部直摸 <see cref="Composition"/>.ControlPool。</summary>
    public System.Windows.Forms.Control? FindControlByName(string controlName)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(controlName))
            return null;

        return _composition.ControlPool?.GetControl(controlName);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EcsGate));
    }

    private async Task<TResult> DispatchAsync<TResult>(EcsOperation<TResult> op, CancellationToken ct)
    {
        return op switch
        {
            // 单轨收敛（P3/RD2）：唯一 operation。旧 LoadConfig/ExecuteRender/QueryControls/ClearScope/
            // RecalculateLayout 族已删除（全仓无构造调用方，死分支随 operation record 一并移除）。
            LoadAndRenderOperation o => ConvertResult(op, await DoLoadAndRender(o).ConfigureAwait(false)),
            _ => throw new NotSupportedException($"Unknown operation: {op.GetType().Name}")
        };
    }

    private static TResult ConvertResult<TResult>(EcsOperation<TResult> op, object? actual)
    {
        if (actual is TResult result)
            return result;
        throw new InvalidOperationException(
            $"操作 {op.OperationName} 返回类型 '{actual?.GetType().FullName ?? "null"}' " +
            $"与期望类型 '{typeof(TResult).FullName}' 不匹配");
    }

    private async Task<RenderRunResult> DoLoadAndRender(LoadAndRenderOperation op)
    {
        if (op.Config == null)
            throw new ArgumentNullException(nameof(op.Config));

        // P7：宿主显式校验——host=null 渲染必然产生孤儿控件（「日志全绿但 UI 空」根因），不再静默创建
        if (op.HostControl == null)
            throw new InvalidOperationException(
                $"scope '{op.Scope}' 宿主控件为空：渲染无法挂载。请先经 ContainerRegistry 统一寻址（scope 优先 + hostKey 反查兜底）解析宿主" +
                "（ContainerRegistry → ControlPool/root 直绑表）后传入宿主控件。");

        // 保持「先销毁→再 Load」时序不变量
        await DestroyScopeAsync(op.Scope).ConfigureAwait(false);

        // M2·D3 修复（渲染事务化）：Load→Creation→Render 原子 + 异常补偿。
        // using 作用域内执行事务体；异常路径 → RenderTransaction.Dispose → Rollback（DestroyScopeEntities 清残留，
        // 绝不 Dispose composition）；状态机 Failed 由 ScopeLifecycleStateMachine 捕获 renderDelegate 异常自动落定（E1）。
        using var transaction = new RenderTransaction(op.Scope, _composition, _compositionLifecycle);

        // F4.1（fail-stop）：渲染段异常 → 快照落 HasError（作用域 Failed 由 StateMachine 捕获落定），
        // 避免异常被吞后下游仍以为渲染成功；rethrow 交 RenderTransaction 补偿回滚。
        try
        {
            // Load/Creation 区间返回的 RootEntity 即该 scope 渲染根实体（RenderLoadStage 于 PartLoad 时回填）。
            // C5：Execute 的 rootContainer 参数已删除（仅日志用），宿主寻址统一经 HostMountService。
            _composition.Execute(op.Config, op.Scope);
            // 物理挂载由渲染管线内 RenderMountStage（Apply→Mount→Bind 时相）完成，此处不再手调 AttachRootToHost
            // （历史时序断链根因）；宿主真实子控件数也已由 RenderMountStage 回填进结果（R5 单算），不再二次覆盖。

            // S3：重渲染限定作用域 scope，消除「Side 加载却全量重放（误清 Content）」缺陷
            // P7：渲染结果（含挂载统计）作为结果契约回传——「渲染执行 ≠ 挂载可见」由 Succeeded 判据暴露
            var result = _composition.ExecuteRenderStage(op.Scope);

            // M6·MarkMounted 单写：挂载状态写入从本处移除——ScopeLifecycleAdapter.RenderAsync ⑥ 为唯一权威点
            // （宿主注入成功处，经 StateCenter 状态层落定）。本处仅回传 HostActualChildCount 供状态层判定。

            // E7：落定本次渲染结果快照（GetLastRenderStats 读此，语义=最近一次渲染区间的真实结果）
            _lastRenderResult = result;

            // 诊断：挂载统计落日志（挂载在 RenderMountStage 已完成，HostActualChildCount 为唯一真值），
            // 供 sideload 渲染「宿主空 / 未挂载」断点定位。
            FrontEndDrivenLog.Log("EcsGate_DoLoadAndRender_mount",
                new
                {
                    scope = op.Scope,
                    succeeded = result.Succeeded,
                    collectedCount = result.CollectedCount,
                    appliedCount = result.AppliedCount,
                    mountedCount = result.MountedCount,
                    orphanCount = result.OrphanCount,
                    hostActualChildCount = result.HostActualChildCount
                });

            // M2：渲染成功落定事务（跳过 Rollback 补偿）
            transaction.Commit();

            return result;
        }
        catch
        {
            // F4.1：异常快照标记 HasError（GetLastRenderStats 读到此哨兵即知"本次渲染失败"）。
            _lastRenderResult = new RenderRunResult { Scope = op.Scope, RenderExecuted = false, HasError = true };
            throw;
        }
    }

    /// <summary>
    /// P3/R2：作用域销毁统一收敛为单一异步实现（删除同步 DestroyScope 双轨）。
    /// 2026-08-05 R3：销毁实体统一经 ICompositionLifecycle.ScopeDestroyed（绝不 Dispose composition）。
    /// </summary>
    private async Task DestroyScopeAsync(string scopeName)
    {
        var platform = _eventPlatform;
        if (platform != null)
        {
            await platform.PublishAsync(PlatformEvent.Destroying(scopeName)).ConfigureAwait(false);
        }

        // 2026-08-06 M4b 收敛：销毁实体统一经 ICompositionLifecycle.ScopeDestroyed（StateCenter 单一路径，绝不 Dispose composition）。
        // 旧 IScopeDestroyer 分支已删除（ConfigurationLifecycleManager 收敛为仅启动编排）；无 StateCenter 装配场景回退 composition 直毁。
        if (_compositionLifecycle != null)
        {
            _compositionLifecycle.ScopeDestroyed(scopeName);
        }
        else
        {
            _composition.DestroyScopeEntities(scopeName);
        }

        if (platform != null)
        {
            await platform.PublishAsync(PlatformEvent.Destroyed(scopeName)).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 2026-08-05 R3（治 L1）：composition 释放唯一经 ICompositionLifecycle.AppShutdown()——
        // StateCenter 收敛路径保证 Provider 已注入（StateServiceCollection → StateCenterFactory → SetCompositionLifecycle）。
        // EcsGate.Dispose 自身绝不触碰 _composition.Dispose()（全局清空 ControlPool/EntityManager 的唯一路径 = AppShutdown）。
        _compositionLifecycle?.AppShutdown();
        _disposed = true;
    }
}

/// <summary>渲染统计只读快照（EcsGate.GetLastRenderStats 返回，宿主/审计经此读计数，不暴露 Composition 内部）。</summary>
public sealed record RenderStats(
    int CollectedCount,
    int AppliedCount,
    double AppliedRatio,
    bool HasExecuted);

public interface IEcsInterceptor
{
    Task OnBefore<TResult>(EcsOperation<TResult> operation, CancellationToken ct);
    Task OnAfter<TResult>(EcsOperation<TResult> operation, TResult result, CancellationToken ct);
    Task OnError<TResult>(EcsOperation<TResult> operation, Exception ex, CancellationToken ct);
}

/// <summary>
/// P3/E5：审计拦截器收敛到统一日志门面（FrontEndDrivenLog），
/// 移除 File.AppendAllText 无界直写与 catch {} 静默吞异常。
/// </summary>
public sealed class AuditLogInterceptor : IEcsInterceptor
{
    public Task OnBefore<TResult>(EcsOperation<TResult> op, CancellationToken ct)
    {
        FrontEndDrivenLog.Log("EcsGate_audit_before",
            new { operationName = op.OperationName, operationId = op.OperationId, timestamp = op.Timestamp });
        return Task.CompletedTask;
    }

    public Task OnAfter<TResult>(EcsOperation<TResult> op, TResult result, CancellationToken ct)
    {
        // P7：结果契约闭合——渲染操作（RenderRunResult）输出挂载统计（mountedCount/orphanCount/succeeded），
        // 替代裸 bool「无异常即成功」的假绿；「渲染执行 ≠ 挂载可见」在审计层即可见。
        object? detail = result switch
        {
            RenderRunResult r => new
            {
                r.CollectedCount,
                r.MountedCount,
                // M3 内容层物理核验挂载数（与 HostActualChildCount 区分，暴露「root 挂上但内容层物理 orphan」）
                r.ContentMounted,
                r.OrphanCount,
                r.Succeeded,
                skipReasons = r.SkipReasons is { Count: > 0 } ? r.SkipReasons : null
            },
            _ => null
        };

        FrontEndDrivenLog.Log("EcsGate_audit_after",
            new
            {
                operationName = op.OperationName,
                operationId = op.OperationId,
                elapsedMs = (DateTime.UtcNow - op.Timestamp).TotalMilliseconds,
                result = result?.ToString(),
                detail
            });
        return Task.CompletedTask;
    }

    public Task OnError<TResult>(EcsOperation<TResult> op, Exception ex, CancellationToken ct)
    {
        FrontEndDrivenLog.Log("EcsGate_audit_error",
            new { operationName = op.OperationName, operationId = op.OperationId, error = ex.Message });
        return Task.CompletedTask;
    }
}

#nullable enable

using System;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared.Framework;
using TConfig.Abstractions;

namespace FrontEndDriven.Pipeline;

/// <summary>
/// 渲染请求类型（M3·双 Kind 收敛）：
/// <see cref="FullLoad"/> = LoadConfig + Render 完整事务（经 StateCenter.Scopes.LoadAsync，M2 事务化）；
/// <see cref="IncrementalRender"/> = 仅渲染区间（ExecuteRenderStage，快照重放）。
/// </summary>
public enum RenderRequestKind
{
    FullLoad,
    IncrementalRender
}

/// <summary>
/// 渲染请求（M3）：scope+version 去重单队列条目。
/// </summary>
public sealed record RenderRequest
{
    public required string Scope { get; init; }

    public required RenderRequestKind Kind { get; init; }

    /// <summary>FullLoad 的配置路径（IncrementalRender 为 null）。</summary>
    public string? ConfigPath { get; init; }

    public string Reason { get; init; } = "request";

    public long Version { get; init; }
}

/// <summary>
/// 渲染请求队列（M5·单入口 + 差异投影 → M3·异步化）：scope+version 串行单队列，UI 线程单消费者。
/// 加载/函数执行/分割条/重算统一入队（Enqueue/EnqueueFullLoad），过期请求按版本号丢弃（同 scope 只执行最新）。
/// 2026-08-06 M3 改造：同步 Drain → UI 线程异步消费（D6）——
///   - Enqueue 不再立即同步执行，返回 <see cref="Task{TResult}"/>（TCS 结果契约，boot/命令链 await 保持同步语义）；
///   - 消费经 IUIDispatcher.Post 回投 UI 线程（WinForms 控件访问安全），async Drain 循环处理执行期间新入队请求；
///   - UI 线程 await 时消息泵继续运行（await 非阻塞），绝不 GetResult（防自锁）。
/// </summary>
public sealed class RenderRequestQueue
{
    private sealed record PendingEntry(RenderRequest Request, TaskCompletionSource<bool> Tcs);

    // scope+version 去重存储收敛到通用 VersionedKeyedQueue（latest-wins）。
    private readonly VersionedKeyedQueue<string, PendingEntry> _latest = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, string?, Task<bool>> _fullLoadRenderer;
    private readonly Func<string, object?> _incrementalRenderer;
    private readonly Func<IUIDispatcher?> _dispatcherResolver;

    /// <param name="dispatcherResolver">UI 线程调度器解析（composition 装配期 dispatcher 未注册，延迟到消费期解析；null → 退化同步 Drain）。</param>
    /// <param name="fullLoad">FullLoad 渲染委托（scope, configPath）→ 是否成功（StateCenter.Scopes.LoadAsync 事务）。</param>
    /// <param name="incremental">IncrementalRender 渲染委托（scope）→ 渲染结果（ExecuteRenderStage）。</param>
    public RenderRequestQueue(
        Func<IUIDispatcher?> dispatcherResolver,
        Func<string, string?, Task<bool>> fullLoad,
        Func<string, object?> incremental)
    {
        _dispatcherResolver = dispatcherResolver ?? throw new ArgumentNullException(nameof(dispatcherResolver));
        _fullLoadRenderer = fullLoad ?? throw new ArgumentNullException(nameof(fullLoad));
        _incrementalRenderer = incremental ?? throw new ArgumentNullException(nameof(incremental));
    }

    public int PendingScopeCount => _latest.Count;

    /// <summary>入队增量渲染请求（同 scope 旧版本自动失效），返回消费完成 TCS（可 await 保持同步语义）。</summary>
    public Task<bool> Enqueue(string scope, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(false);

        return EnqueueCore(new RenderRequest
        {
            Scope = scope ?? string.Empty,
            Kind = RenderRequestKind.IncrementalRender,
            Reason = reason
        });
    }

    /// <summary>入队完整加载渲染请求（LoadConfig+Render 事务），返回消费完成 TCS。</summary>
    public Task<bool> EnqueueFullLoad(string scope, string configPath, string reason)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return Task.FromResult(false);

        return EnqueueCore(new RenderRequest
        {
            Scope = scope ?? string.Empty,
            Kind = RenderRequestKind.FullLoad,
            ConfigPath = configPath,
            Reason = reason
        });
    }


    private Task<bool> EnqueueCore(RenderRequest request)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 版本号由通用 VersionedKeyedQueue 唯一分配（latest-wins），请求去重比较统一经队列条目版本。
        _latest.Register(request.Scope, new PendingEntry(request, tcs));

        FrontEndDrivenLog.Log("RenderRequestQueue_Enqueue",
            new { scope = request.Scope, kind = request.Kind.ToString(), reason = request.Reason, pendingScopes = _latest.Count });

        ScheduleDrain();
        return tcs.Task;
    }

    /// <summary>调度单消费者：经 UI 线程回投（Post），由消息泵串行执行（async Drain 循环吞掉执行期新请求）。
    /// 调度器未注册（测试/装配期）→ 退化同步 Drain（Enqueue 线程直接消费，保持同步语义）。</summary>
    private void ScheduleDrain()
    {
        var dispatcher = _dispatcherResolver();
        if (dispatcher == null)
        {
            _ = DrainAsync();
            return;
        }

        dispatcher.Post(() => _ = DrainAsync());
    }

    /// <summary>
    /// 单消费者（UI 线程消息泵）：对每个有待执行请求的 scope，仅执行最新版本（过期丢弃），执行后出队并完成 TCS。
    /// drain 循环骨架（重入守卫 / 快照 / 版本去重 / try/finally / while 吞执行期新请求）收敛到
    /// <see cref="VersionedKeyedQueue{TKey,TEntry}.DrainAllAsync"/>；本方法仅提供「最新版本执行」与「过期条目完成 TCS」两个委托。
    /// E3 修复：async void → async Task——异常不逃逸到 void lambda 崩溃进程
    /// （渲染失败已由内部 catch 捕获并 TrySetResult(false)，主循环 try/finally 保护）。
    /// </summary>
    private async Task DrainAsync()
    {
        await _latest.DrainAllAsync(
            execute: async (scope, entry, version) =>
            {
                try
                {
                    bool success = entry.Request.Kind switch
                    {
                        RenderRequestKind.FullLoad => await _fullLoadRenderer(scope, entry.Request.ConfigPath).ConfigureAwait(true),
                        _ => RunIncremental(scope, entry.Request)
                    };

                    if (!success)
                    {
                        FrontEndDrivenLog.Log("RenderRequestQueue_render_unsuccessful",
                            new { scope, kind = entry.Request.Kind.ToString(), reason = entry.Request.Reason });
                    }

                    entry.Tcs.TrySetResult(success);
                }
                catch (Exception ex)
                {
                    FrontEndDrivenLog.Log("RenderRequestQueue_render_failed",
                        new { scope, kind = entry.Request.Kind.ToString(), reason = entry.Request.Reason, error = ex.Message });
                    entry.Tcs.TrySetResult(false);
                }

                return true; // 渲染队列无 break 语义，恒继续遍历
            },
            onStale: (scope, entry) => entry.Tcs.TrySetResult(false),
            maxPasses: QueueDrainPolicy.MaxPassesPerDrain);
    }

    private bool RunIncremental(string scope, RenderRequest request)
    {
        var result = _incrementalRenderer(scope);
        FrontEndDrivenLog.Log("RenderRequestQueue_incremental_done",
            new { scope, reason = request.Reason, result = result?.ToString() });
        return result != null;
    }
}

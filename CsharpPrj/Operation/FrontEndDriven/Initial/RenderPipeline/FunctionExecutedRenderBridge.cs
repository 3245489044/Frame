#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FrontEndDriven.Eventing;
using FrontEndDriven.Pipeline;
using FrameworkAbstractions.ComponentRegistry;
using Share.EventBindingOrchestrator.Execution;
using TConfig.Abstractions;

namespace FrontEndDriven.Initial.RenderPipeline;

public sealed class FunctionExecutedRenderBridge : IDisposable
{
    /// <summary>默认重渲染封顶次数（对齐 L3 配置惯例；可由构造参数覆盖）。</summary>
    public const int DefaultMaxRerenderCount = 3;

    /// <summary>默认重置窗口：窗口内无新事件则计数归零（防"只增不重置"永久跳过）。</summary>
    public static readonly TimeSpan DefaultResetWindow = TimeSpan.FromSeconds(1);

    private readonly IUIDispatcher _dispatcher;
    private readonly IDisposable _subscription;
    private readonly int _maxRerenderCount;
    private readonly TimeSpan _resetWindow;

    private int _pendingRerenders;

    private int _rerenderCount;

    private long _lastRerenderTicks;

    private int _disposed;

    public FunctionExecutedRenderBridge(
        EventPlatform platform,
        IUIDispatcher dispatcher,
        int maxRerenderCount = DefaultMaxRerenderCount,
        TimeSpan? resetWindow = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _maxRerenderCount = maxRerenderCount > 0 ? maxRerenderCount : DefaultMaxRerenderCount;
        _resetWindow = resetWindow ?? DefaultResetWindow;

        var p = platform ?? throw new ArgumentNullException(nameof(platform));
        _subscription = p.SubscribeAsync(EventTypeNames.FunctionExecuted, OnFunctionExecutedAsync);
    }

    private Task OnFunctionExecutedAsync(IEvent evt)
    {
        // S4：bridge 已释放（应用级 composition 释放后）→ 残留事件的直接终止，
        // 避免 composition.Dispose 后仍触发 ExecuteRenderStage 抛 ObjectDisposedException（缺陷5）。
        // 2026-08-05 StateCenter 收敛（治 L1）：composition 释放唯一经 AppShutdown（进程退出），
        // 本 _disposed 竞态补丁保留为防御性（L1 根因消除后不再触发，P6 按回归结果决定去留）。
        if (Volatile.Read(ref _disposed) == 1)
            return Task.CompletedTask;

        // B4：加载类函数（LoadConfigToTarget/LoadConfigToScope 等，经 FunctionCallFacade 执行）期间，
        // LoadAndRender 已在 EcsGate 内完成完整渲染（Load→Creation→Collect→Apply→Mount→Bind）。
        // 若此处再触发 ExecuteRenderStage(scope)，与 LoadAndRender 的渲染在毫秒级窗口内叠加，
        // 形成同 scope 双渲染（既有故障：Side 加载后全量快照重复收集与重放）。
        // 阶段2.2：裁决改读函数副作用元数据（RenderingAffinity.SelfRendered），替代 StartsWith("LoadConfig") 前缀黑魔法；
        // 其余函数（如 LoadJsonConfigList 填充列表）保持重放快照语义。
        if (evt.Payload is FunctionExecutedPayload { RenderingAffinity: RenderingAffinity.SelfRendered })
        {
            return Task.CompletedTask;
        }

        var now = Environment.TickCount64;
        var lastTicks = Interlocked.Read(ref _lastRerenderTicks);

        // 重置窗口：距上次事件超过窗口 → 计数归零（封顶仅作用连续事件突发，非永久跳过）
        if (lastTicks != 0 && now - lastTicks > (long)_resetWindow.TotalMilliseconds)
        {
            Interlocked.Exchange(ref _rerenderCount, 0);
        }
        Interlocked.Exchange(ref _lastRerenderTicks, now);

        if (Interlocked.Increment(ref _rerenderCount) > _maxRerenderCount)
            return Task.CompletedTask;

        if (Interlocked.Exchange(ref _pendingRerenders, 1) == 1)
            return Task.CompletedTask;

        // 缺陷2 修复：按事件携带的 scope 限定重渲染。
        // 函数执行事件通常由某作用域的函数调用触发（如 LoadConfigToTarget 在 Side 作用域），
        // 事件 scope 非空时仅重渲染该作用域（配合 S3 收集器作用域过滤），
        // 空 scope 保持全量语义（向后兼容，如无作用域上下文的通用函数执行）。
        var scope = evt.Scope;
        _dispatcher.Post(() =>
        {
            try
            {
                // Post 回调与 Dispose 之间可能存在竞态（dispose 发生在队列消费前）→ 双重防护
                if (Volatile.Read(ref _disposed) == 1)
                    return;

                // M5·单入口收敛：重渲染统一经 RenderRequestQueue（scope+version 去重，单消费者），
                // 消除「函数执行事件 → 直接 ExecuteRenderStage」与加载/分割条/重算入口的竞态叠加。
                // scope 为 null/空 时入队全量（ExecuteRenderStage 语义：null/空 = 全量）。
                // C11：RenderRequestQueue 由 PipelineCompositionFactory 恒注册，原 else 回退分支为死代码已删除。
                var renderQueue = AppServiceProvider.Resolve<RenderRequestQueue>()
                    ?? throw new InvalidOperationException("RenderRequestQueue 未注册到 AppServiceProvider");
                renderQueue.Enqueue(scope ?? string.Empty, "function-executed");
            }
            catch (ObjectDisposedException)
            {
                // 竞态兜底：composition 已在回调执行前被释放，静默跳过（应用级生命周期收敛）
            }
            finally
            {
                Interlocked.Exchange(ref _pendingRerenders, 0);
            }
        });

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
        Interlocked.Exchange(ref _disposed, 1);
    }
}
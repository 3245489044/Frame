#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 输出协调产物：追踪号 + 三段耗时（gather / plan / total）+ 分域耗时账 + 最终产物。
/// <para>字段名与领域日志 <c>ConversationView_RefreshTrace</c> 一一对应（禁改名）。</para>
/// </summary>
public sealed record RenderOutputTiming(
    string TraceId,
    long GatherMs,
    long PlanMs,
    long TotalMs,
    IReadOnlyDictionary<string, long> Phases,
    RenderOutput Output);

/// <summary>
/// 渲染输出协调器（通用件，唯一实现）：把「追踪号生成 + 分段计时 + 异常收敛为 Skip + 分域耗时账」
/// 从各入口（刷新 / 显式交互 / 数据库页）收敛到一处。
/// <para>
/// 领域只提供 <c>gatherAsync</c>（取数/装配）与 <c>planAsync</c>（区域计划/产物）；
/// 异常一律收敛为 <see cref="RenderOutput.Skip"/>（不整页兜底），由 <c>onFailure</c> 留诊断。
/// 追踪号工厂由调用方**必填注入**（本层不依赖任何上层刷新框架类型）。
/// </para>
/// </summary>
public static class RenderOutputCoordinator
{
    /// <summary>追踪号：宿主未给 ⇒ 按写回来源前缀经注入工厂生成（交互不得冒充 refresh-N）。</summary>
    /// <param name="traceFactory">追踪号工厂（必填；入参 = 来源前缀，连接层注入刷新框架 <c>RefreshTraceId.New</c> 或宿主自有工厂）。</param>
    public static string ResolveTraceId(UiWriteSource source, string? traceId, Func<string, string> traceFactory)
    {
        ArgumentNullException.ThrowIfNull(traceFactory);
        return string.IsNullOrWhiteSpace(traceId) ? traceFactory(PrefixOf(source)) : traceId!;
    }

    /// <summary>写回来源 → 追踪号前缀（受控词单源）。</summary>
    public static string PrefixOf(UiWriteSource source)
        => source switch
        {
            UiWriteSource.Interactive => "interactive",
            UiWriteSource.Tick => "tick",
            UiWriteSource.Stream => "stream",
            UiWriteSource.Mount => "mount",
            _ => "refresh",
        };

    /// <summary>
    /// 执行一拍：计时取数 → 计时计划；任一阶段异常 ⇒ <see cref="RenderOutput.Skip"/>。
    /// <paramref name="phases"/> 为调用方持有的分域耗时账（null ⇒ 不记账但仍计时）。
    /// </summary>
    public static async Task<RenderOutputTiming> BuildAsync(
        UiWriteSource source,
        string? traceId,
        Dictionary<string, long>? phases,
        Func<PhaseTimer, Task> gatherAsync,
        Func<Task<RenderOutput>> planAsync,
        Action<string, Exception>? onFailure,
        Func<string, string> traceFactory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gatherAsync);
        ArgumentNullException.ThrowIfNull(planAsync);

        var trace = ResolveTraceId(source, traceId, traceFactory);
        var timer = new PhaseTimer(phases);
        var started = Environment.TickCount64;
        var gatherDoneAt = started;
        long gatherMs;

        try
        {
            await gatherAsync(timer).ConfigureAwait(false);
            gatherDoneAt = Environment.TickCount64;
            gatherMs = gatherDoneAt - started;
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(trace, ex);
            var failedAt = Environment.TickCount64;
            return new RenderOutputTiming(trace, failedAt - started, 0, failedAt - started, timer.Phases, RenderOutput.Skip);
        }

        RenderOutput output;
        try
        {
            output = await planAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(trace, ex);
            output = RenderOutput.Skip;
        }

        var finished = Environment.TickCount64;
        return new RenderOutputTiming(trace, gatherMs, finished - gatherDoneAt, finished - started, timer.Phases, output);
    }
}

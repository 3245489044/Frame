#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 内容写回协调器（L0 写回入口，D2 单写者）——有宿主协调器（<see cref="Writer"/>）⇒ 经协调器落页（不重载文档，保态）；
/// 无协调器 ⇒ 回退整页渲染。fire-and-forget 出口经 <see cref="AsyncObserve"/> 观察异常，不以未观察 Task 逃逸（F2）。
/// </summary>
public sealed class ContentWriteCoordinator
{
    private readonly DiagnosticPort _diagnostics = new();

    public ContentWriteCoordinator(Action<string, string>? diagnostics = null) => _diagnostics.Sink = diagnostics;

    /// <summary>宿主内容写回协调器（D2 单写者）；未装配为 null。</summary>
    public Func<string, Task>? Writer { get; set; }

    /// <summary>写回计划执行器（单写落地）；未装配为 null。</summary>
    public Func<UiWritePlan, Task>? PlanExecutor { get; set; }

    /// <summary>内容写回：有协调器且内容非空 ⇒ 经协调器落页；否则经 <paramref name="renderFallback"/> 整页渲染。</summary>
    public Task WriteAsync(string? html, CancellationToken ct, Func<string?, CancellationToken, Task> renderFallback)
        => Writer is { } writer && !string.IsNullOrEmpty(html)
            ? writer(html)
            : renderFallback(html, ct);

    /// <summary>值契约 / 同步出口触发的写回（fire-and-forget）：失败经诊断出口上报，不以未观察 Task 异常逃逸。</summary>
    public void WriteDetached(string? html, Func<string?, CancellationToken, Task> renderFallback)
        => AsyncObserve.Run(WriteAsync(html, default, renderFallback),
            ex => _diagnostics.Report("HtmlChannel_writeback_observed_error", $"{ex.GetType().Name}: {ex.Message}"));
}

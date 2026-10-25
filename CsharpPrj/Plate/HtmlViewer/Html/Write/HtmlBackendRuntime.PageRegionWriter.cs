#nullable enable

using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 页面区域写端口实现（R2）：区域目标由本运行时转写回计划，经既有单写者（<see cref="ExecuteAsync"/>）落地；
/// 不新增 DOM 写路径，保持「一个写协调器」不变量。
/// </summary>
public sealed partial class HtmlBackendRuntime : IPageRegionWriter
{
    /// <summary>页面区域声明目录（装配期由组合根注入；null = 页面未声明 regions）。</summary>
    public RegionCatalog? PageRegions { get; set; }

    RegionCatalog? IPageRegionWriter.Regions => PageRegions;

    /// <summary>元素归属：域限定键（<c>page:x</c>/<c>host:x</c>）在本运行时的声明索引中命中。</summary>
    public bool OwnsElement(string elementName)
        => !string.IsNullOrWhiteSpace(elementName) && _controlsByName.ContainsKey(elementName);

    /// <summary>区域是否声明为流式段（append-only）。</summary>
    public bool IsStreamRegion(string regionKey)
        => PageRegions?.Resolve(regionKey)?.Stream == true;

    /// <summary>执行计划（单写者入口；来源取计划自述，缺省 Interactive）。</summary>
    public Task<UiWriteResult> ExecutePlanAsync(UiWritePlan plan, string? traceId = null, CancellationToken ct = default)
        => ExecuteAsync(plan, plan.Source, traceId, ct);

    /// <summary>区域写唯一入口：覆盖 → <see cref="UiWritePlan.Region"/>；流式 → <see cref="UiWritePlan.Append"/>。</summary>
    public Task<UiWriteResult> WriteRegionAsync(
        string regionKey,
        string html,
        bool append,
        string? traceId = null,
        CancellationToken ct = default)
    {
        var fingerprint = _fingerprint(html);
        UiWritePlan plan = append
            ? UiWritePlan.Append(
                regionKey,
                new[] { new UiStreamItem(fingerprint, html) },
                cap: null,
                direction: UiStreamDirection.OldestFirst,
                source: UiWriteSource.Interactive)
            : UiWritePlan.Region(regionKey, html, fingerprint, UiWriteSource.Interactive);

        return ExecuteAsync(plan, UiWriteSource.Interactive, traceId, ct);
    }
}
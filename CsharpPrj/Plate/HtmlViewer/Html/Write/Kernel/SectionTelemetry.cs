#nullable enable

using System;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 节/拍遥测（D6 的修复，判据载体）：每节一行含 nodes/bytes/jsMs/outcome，每拍一行含
/// batchNodes/batchBytes/lane/queueMs/execMs/droppedSections/degradeReason。字段名稳定供 grep 复算。
/// </summary>
public sealed class SectionTelemetrySink
{
    /// <summary>遥测出口（宿主接线到日志门面；未接线为空操作）。</summary>
    public Action<string>? Sink { get; set; }

    /// <summary>单节一行。</summary>
    public void OnSection(SectionOutcome outcome)
        => Sink?.Invoke(FormatSection(outcome));

    /// <summary>每拍一行。</summary>
    public void OnBeat(UiWriteSource source, int nodes, int bytes, long queueMs, long execMs, int droppedSections, string? degradeReason)
        => Sink?.Invoke(FormatBeat(source, nodes, bytes, queueMs, execMs, droppedSections, degradeReason));

    /// <summary>节行格式（纯函数，供单测/复算）。</summary>
    public static string FormatSection(SectionOutcome o)
        => $"section={o.Id.Surface}/{o.Id.Key} mode={o.Mode} nodes={o.Nodes} bytes={o.Bytes} jsMs={o.JsMs} outcome={o.Outcome}"
           + (string.IsNullOrEmpty(o.Reason) ? string.Empty : $" reason={o.Reason}");

    /// <summary>拍行格式（纯函数，供单测/复算）。</summary>
    public static string FormatBeat(UiWriteSource source, int nodes, int bytes, long queueMs, long execMs, int droppedSections, string? degradeReason)
        => $"batch source={source} batchNodes={nodes} batchBytes={bytes} lane={SurfaceLane.Of(source)} " +
           $"queueMs={queueMs} execMs={execMs} droppedSections={droppedSections} degradeReason={(string.IsNullOrEmpty(degradeReason) ? "-" : degradeReason)}";
}

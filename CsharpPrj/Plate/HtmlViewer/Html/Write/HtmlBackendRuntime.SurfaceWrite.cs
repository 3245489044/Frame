#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 节批量写入口（L1 唯一写面实现）：一拍一个 batch = **一次**跨进程批量脚本；预算以节点数为主；
/// 车道只决定准入/丢弃（同控件仍单串行，保护单写者不变量）；每节/每拍必产遥测。
/// </summary>
public sealed partial class HtmlBackendRuntime : ISurfaceWriter
{
    private static readonly JsonSerializerOptions BatchJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SectionTelemetrySink _sectionTelemetry = new();

    /// <summary>节/拍遥测出口（宿主接线到日志门面；未接线为空操作）。</summary>
    public Action<string>? SectionTelemetry
    {
        get => _sectionTelemetry.Sink;
        set => _sectionTelemetry.Sink = value;
    }

    /// <summary>节键集合水位（**仅供 Bulk 判定**，不参与去重/写裁决；与 WriteLedger 职责分离）。</summary>
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<string>> _sectionKeys = new(StringComparer.Ordinal);

    private sealed record PendingSection(SectionPlan Plan, object Op, string Slot, long Version, string Fingerprint);

    /// <summary>提交一批节：预算 → 去重准入 → 一次批量脚本 → 逐节聚合结果代数。</summary>
    public async ValueTask<UiWriteResult> SubmitAsync(
        IReadOnlyList<SectionPlan> batch, UiWriteSource source, string? traceId, CancellationToken ct = default)
    {
        var trace = string.IsNullOrWhiteSpace(traceId) ? _traceFactory("section") : traceId;
        var applied = new List<string>();
        var unchanged = new List<string>();
        var failed = new List<string>();
        var anomalies = new List<string>();

        // 整页（首挂载）不可批量：单独落地，不进批量脚本。
        var sections = new List<SectionPlan>(batch.Count);
        foreach (var s in batch)
        {
            if (!s.FullDocument)
            {
                sections.Add(s);
                continue;
            }

            var status = await WriteFullPageAsync(s.Html ?? string.Empty, ct).ConfigureAwait(false);
            var ok = status != HtmlWriteBackStatus.Failed;
            if (ok) applied.Add(s.Name); else failed.Add(s.Name);
            _sectionTelemetry.OnSection(new SectionOutcome(s.Id, s.Mode, s.Nodes, s.Bytes, 0, ok ? "applied" : "failed"));
        }

        var maxNodes = source == UiWriteSource.Interactive || _options.MaxNodesPerBeatBackground <= 0
            ? _options.MaxNodesPerBeat
            : Math.Min(_options.MaxNodesPerBeat, _options.MaxNodesPerBeatBackground);
        var budget = new WriteBudget(maxNodes, _options.MaxBytesPerFlush, _options.MaxMsPerFlush);
        // F5：按阶段排序（壳 → 结构 → 内容 → 装饰）；预算在装饰阶段可先行丢弃。
        var ordered = sections
            .OrderBy(static s => s.Stage)
            .ToList();
        var decision = WriteBudgetPolicy.Decide(budget, ordered, source, _options.ProgressiveSurface);

        if (decision.Dropped.Count > 0)
        {
            _metrics.OnDropped(decision.DegradeReason ?? "section-budget", decision.Dropped.Count);
            Report("EventBridge_writeback",
                $"节预算降级：kept={decision.Kept.Count} dropped={decision.Dropped.Count} nodes={decision.Nodes} " +
                $"bytes={decision.Bytes} reason={decision.DegradeReason} source={source} trace={trace}");
            foreach (var d in decision.Dropped)
            {
                // 丢弃即失败可见（不进 unchanged：未触 DOM 不等于「内容未变」）。
                failed.Add(d.Name);
                _metrics.OnSection(d.Mode, applied: false);
                _sectionTelemetry.OnSection(new SectionOutcome(d.Id, d.Mode, d.Nodes, d.Bytes, 0, "dropped", decision.DegradeReason));
            }
        }

        var ops = new List<object>(decision.Kept.Count);
        var pending = new List<PendingSection>(decision.Kept.Count);
        foreach (var s in decision.Kept)
        {
            var prepared = Prepare(s, unchanged);
            if (prepared is null) continue;
            ops.Add(prepared.Op);
            pending.Add(prepared);
        }

        if (ops.Count == 0)
            return WriteOutcome.Classify(applied, unchanged, failed, anomalies.Count > 0 ? anomalies : null).ToResult(trace);

        var started = Environment.TickCount64;
        var script = "window.__uiReconcile.batch(" + JsonSerializer.Serialize(ops, BatchJsonOptions) + ")";
        var raw = HtmlElementNode.UnwrapString(await _channel.ExecuteScriptAsync(script, ct).ConfigureAwait(false));
        var jsMs = Environment.TickCount64 - started;
        var statuses = ParseBatchStatuses(raw, pending.Count);
        Accumulate(pending, statuses, jsMs, applied, failed, anomalies);
        _metrics.OnBeat(SumNodes(pending, statuses), SumBytes(pending, statuses), decision.Dropped.Count, decision.DegradeReason);
        _metrics.OnQueueWait(UiThreadMarshaler.LastQueueWaitMs);

        Report("EventBridge_writeback",
            $"节批量写回 source={source} sections={batch.Count} kept={decision.Kept.Count} applied={applied.Count} " +
            $"unchanged={unchanged.Count} failed={failed.Count} jsMs={jsMs} dropped={decision.Dropped.Count} " +
            $"reason={decision.DegradeReason ?? "-"} trace={trace}");

        return WriteOutcome.Classify(applied, unchanged, failed, anomalies.Count > 0 ? anomalies : null).ToResult(trace);
    }
}

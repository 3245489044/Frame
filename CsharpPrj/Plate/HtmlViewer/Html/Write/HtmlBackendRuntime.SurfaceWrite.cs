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

        // F-4 同拍同目标归并：一拍内同 name 只允许一次落地（后者为准），防「前者写后被后者空载荷清空」。
        var merged = 0;
        var kept = new List<SectionPlan>(decision.Kept.Count);
        var lastAt = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var section in decision.Kept)
        {
            if (lastAt.TryGetValue(section.Name, out var at))
            {
                kept[at] = section;
                merged++;
                continue;
            }

            lastAt[section.Name] = kept.Count;
            kept.Add(section);
        }

        if (merged > 0)
            Report("EventBridge_writeback", $"同拍同目标归并：merged={merged} kept={kept.Count} trace={trace}");

        var ops = new List<object>(kept.Count);
        var pending = new List<PendingSection>(kept.Count);
        foreach (var s in kept)
        {
            var prepared = Prepare(s, unchanged, failed);
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
        // F-8 写后取证（只读诊断；不参与 Applied/Failed 裁决，异常自吞并可见化）。
        await ProbeWriteBackAsync(pending, statuses, source, trace, ct).ConfigureAwait(false);

        Report("EventBridge_writeback",
            $"节批量写回 source={source} sections={batch.Count} kept={decision.Kept.Count} applied={applied.Count} " +
            $"unchanged={unchanged.Count} failed={failed.Count} jsMs={jsMs} dropped={decision.Dropped.Count} " +
            $"reason={decision.DegradeReason ?? "-"} trace={trace}");

        return WriteOutcome.Classify(applied, unchanged, failed, anomalies.Count > 0 ? anomalies : null).ToResult(trace);
    }

    /// <summary>
    /// 写后 DOM 取证（诊断；F-8）：一次脚本查询全部已写目标 + 文档级计数，与载荷侧 mode/nodes/bytes/status 同行输出。
    /// <para>只读、不参与裁决；异常吞并可见化（诊断不得改变写回结论）。仅交互来源执行（刷新/心跳零额外往返）。</para>
    /// </summary>
    private async Task ProbeWriteBackAsync(
        List<PendingSection> pending, string[] statuses, UiWriteSource source, string trace, CancellationToken ct)
    {
        if (source != UiWriteSource.Interactive || pending.Count == 0) return;
        var names = new List<string>(pending.Count + 1);
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < pending.Count; i++)
        {
            var plan = pending[i].Plan;
            if (string.IsNullOrEmpty(plan.Name)) continue;
            if (!payload.ContainsKey(plan.Name)) names.Add(plan.Name);
            payload[plan.Name] = $"{plan.Mode}/{plan.Nodes}/{plan.Bytes}/{statuses[i]}";
        }

        if (names.Count == 0) return;
        // 壳容器纳入取证（H3 判别：区域内容是否错位到 region-main）。
        if (!names.Contains("region-main")) names.Add("region-main");
        try
        {
            var script = "(function(){" + DomOps.LookupFunction + "var ns=" + JsonSerializer.Serialize(names) + ";"
                + "var all=document.querySelectorAll('[data-name]');var out=[];"
                + "for(var i=0;i<ns.length;i++){var el=_wfFind(ns[i]);"
                + "var dn=el?el.querySelectorAll('[data-name]'):[];var f=dn.length?dn[0].getAttribute('data-name'):'';"
                + "out.push({n:ns[i],c:el?el.children.length:-1,d:dn.length,f:f,t:el?((el.textContent||'').length):-1,h:el?el.innerHTML.length:-1});}"
                + "var body=(document.body&&document.body.innerHTML)?document.body.innerHTML.length:0;"
                + "return JSON.stringify({r:out,docNames:all.length,body:body});})()";
            var raw = HtmlElementNode.UnwrapString(await _channel.ExecuteScriptAsync(script, ct).ConfigureAwait(false));
            Report("EventBridge_writeback", BuildProbeLine(raw, payload, trace));
        }
        catch (Exception ex)
        {
            Report("EventBridge_writeback", $"写后取证: probe_failed {ex.GetType().Name} trace={trace}");
        }
    }

    private static string BuildProbeLine(string raw, IReadOnlyDictionary<string, string> payload, string trace)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"写后取证: probe_empty trace={trace}";
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var sb = new System.Text.StringBuilder("写后取证: trace=").Append(trace);
            if (root.TryGetProperty("docNames", out var docNames)) sb.Append(" docNames=").Append(docNames.GetInt32());
            if (root.TryGetProperty("body", out var body)) sb.Append(" body=").Append(body.GetInt32());
            if (root.TryGetProperty("r", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var name = row.TryGetProperty("n", out var n) ? n.GetString() ?? "?" : "?";
                    sb.Append(" | ").Append(name)
                      .Append(" payload=").Append(payload.TryGetValue(name, out var p) ? p : "-")
                      .Append(" domChildren=").Append(row.TryGetProperty("c", out var c) ? c.GetInt32() : -1)
                      .Append(" dataNames=").Append(row.TryGetProperty("d", out var d) ? d.GetInt32() : -1)
                      .Append(" first=").Append(row.TryGetProperty("f", out var f) ? f.GetString() ?? string.Empty : string.Empty)
                      .Append(" textLen=").Append(row.TryGetProperty("t", out var t) ? t.GetInt32() : -1)
                      .Append(" htmlBytes=").Append(row.TryGetProperty("h", out var h) ? h.GetInt32() : -1);
                }
            }

            return sb.ToString();
        }
        catch (JsonException)
        {
            return $"写后取证: probe_unparsable raw={(raw.Length > 160 ? raw.Substring(0, 160) : raw)} trace={trace}";
        }
    }
}

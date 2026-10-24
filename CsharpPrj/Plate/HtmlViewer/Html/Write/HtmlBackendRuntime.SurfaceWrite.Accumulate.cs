#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UiBackend.Html;

public sealed partial class HtmlBackendRuntime
{
    /// <summary>逐节结果聚合（唯一裁决在 <see cref="WriteOutcome.Classify"/>；此处只分类记账）。</summary>
    private void Accumulate(
        List<PendingSection> pending, string[] statuses, long jsMs,
        List<string> applied, List<string> failed, List<string> anomalies)
    {
        for (var i = 0; i < pending.Count; i++)
        {
            var p = pending[i];
            var st = statuses[i];
            if (st == "OK")
            {
                if (!string.IsNullOrEmpty(p.Slot)) _ledger.Commit(p.Slot, p.Version, p.Fingerprint);
                applied.Add(p.Plan.Name);
                _metrics.OnSection(p.Plan.Mode, applied: true);
                _sectionTelemetry.OnSection(new SectionOutcome(p.Plan.Id, p.Plan.Mode, p.Plan.Nodes, p.Plan.Bytes, jsMs, "applied"));
                if (p.Plan.Tree is not null && p.Plan.Mode != SectionWriteMode.BulkReplace)
                    _sectionKeys[TreeSlot(p.Plan.Name)] = BulkThresholdPolicy.CollectKeys(p.Plan.Tree);
                continue;
            }

            if (st.StartsWith("OK:", StringComparison.Ordinal))
            {
                // 对齐异常（重复键等）：不判成功（判据不合格 ⇒ 不放行），异常全链可见。
                var detail = st.Length > 3 ? st.Substring(3) : "anomaly";
                if (!string.IsNullOrEmpty(p.Slot)) _ledger.MarkFailure(p.Slot, p.Fingerprint);
                anomalies.Add($"{p.Plan.Name}:{detail}");
                failed.Add(p.Plan.Name);
                _metrics.OnSection(p.Plan.Mode, applied: false);
                _sectionTelemetry.OnSection(new SectionOutcome(p.Plan.Id, p.Plan.Mode, p.Plan.Nodes, p.Plan.Bytes, jsMs, "anomaly", detail));
                continue;
            }

            if (!string.IsNullOrEmpty(p.Slot)) _ledger.MarkFailure(p.Slot, p.Fingerprint);
            failed.Add(p.Plan.Name);
            _metrics.OnSection(p.Plan.Mode, applied: false);
            _sectionTelemetry.OnSection(new SectionOutcome(p.Plan.Id, p.Plan.Mode, p.Plan.Nodes, p.Plan.Bytes, jsMs, "failed", st));
        }
    }

    private static int SumNodes(List<PendingSection> pending, string[] statuses)
    {
        var nodes = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            if (statuses[i] == "OK") nodes += Math.Max(0, pending[i].Plan.Nodes);
        }

        return nodes;
    }

    private static int SumBytes(List<PendingSection> pending, string[] statuses)
    {
        var bytes = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            if (statuses[i] == "OK") bytes += Math.Max(0, pending[i].Plan.Bytes);
        }

        return bytes;
    }

    /// <summary>批量回串解析：<c>BATCH:[...]</c> ⇒ 逐节状态；其余回串（桥未就绪/未知）对每节生效（不假装成功）。</summary>
    private static string[] ParseBatchStatuses(string? raw, int count)
    {
        var result = new string[count];
        var fallback = string.IsNullOrEmpty(raw) ? "FAIL" : raw;
        if (!string.IsNullOrEmpty(raw) && raw.StartsWith("BATCH:", StringComparison.Ordinal))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(raw.Substring(6));
                if (arr is not null)
                {
                    for (var i = 0; i < count && i < arr.Length; i++) result[i] = arr[i];
                }
            }
            catch (JsonException)
            {
                // 回串不可解析 ⇒ 逐节按失败处置（显式，不静默当成功）。
            }
        }

        for (var i = 0; i < count; i++) result[i] ??= fallback;
        return result;
    }
}

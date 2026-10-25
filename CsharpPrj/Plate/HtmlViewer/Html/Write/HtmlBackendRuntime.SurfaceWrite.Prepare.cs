#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace UiBackend.Html;

public sealed partial class HtmlBackendRuntime
{
    /// <summary>单节准入与载荷打包（去重经唯一账本 <see cref="WriteLedger"/>；Bulk 由阈值策略判定）。
    /// <paramref name="failed"/> 非空时，载荷自洽闸拒绝的节记入失败清单（可见化，不静默丢弃）。</summary>
    private PendingSection? Prepare(SectionPlan s, List<string> unchanged, List<string>? failed = null)
    {
        switch (s)
        {
            case { Stream: { } append }:
            {
                var json = JsonSerializer.Serialize(append, StreamJsonOptions);
                var fp = _fingerprint("append|" + json);
                var slot = StreamSlot(s.Name);
                if (!Admit(slot, fp, s, unchanged, out var version)) return null;
                return new PendingSection(s, new { op = "append", name = s.Name, payload = (object)append }, slot, version, fp);
            }

            case { Trim: { } trim }:
            {
                var json = JsonSerializer.Serialize(trim, StreamJsonOptions);
                var fp = _fingerprint("trim|" + json);
                var slot = StreamSlot(s.Name);
                if (!Admit(slot, fp, s, unchanged, out var version)) return null;
                return new PendingSection(s, new { op = "trim", name = s.Name, payload = (object)trim }, slot, version, fp);
            }

            case { Tree: { } tree }:
            {
                var slot = TreeSlot(s.Name);
                var fp = HtmlTreeWriter.Fingerprint(tree, s.Interaction);
                var keys = BulkThresholdPolicy.CollectKeys(tree);
                _sectionKeys.TryGetValue(slot, out var oldKeys);
                var ratio = BulkThresholdPolicy.ChangeRatio(oldKeys, keys);
                var mode = BulkThresholdPolicy.Decide(
                    _options.BulkReplaceEnabled, s.Nodes, ratio, _options.BulkNodeThreshold, _options.BulkRatioThreshold);
                var bulkHtml = mode == SectionWriteMode.BulkReplace ? UiNodeHtml.ToHtml(tree) : null;
                // F-1/F-2 写面自洽闸：输入有节点而序列化为空 ⇒ 禁止落地（显式失败，不静默清空目标）。
                if (mode == SectionWriteMode.BulkReplace
                    && string.IsNullOrWhiteSpace(bulkHtml)
                    && SectionPlanAdapter.CountNodes(tree) > 1)
                {
                    failed?.Add(s.Name);
                    _metrics.OnSection(mode, applied: false);
                    _sectionTelemetry.OnSection(new SectionOutcome(s.Id, mode, s.Nodes, s.Bytes, 0, "failed", "empty-payload"));
                    Report("EventBridge_writeback",
                        $"空载荷拒绝落地：{s.Name} nodes={s.Nodes} keys={keys.Count} mode=bulk（序列化为空 ⇒ 不写，保留目标现值）");
                    return null;
                }

                if (!Admit(slot, fp, s, unchanged, out var version)) return null;

                object op = mode == SectionWriteMode.BulkReplace
                    ? new { op = "bulk", name = s.Name, html = bulkHtml }
                    : new { op = "tree", name = s.Name, tree, state = s.Interaction };
                return new PendingSection(s with { Mode = mode }, op, slot, version, fp);
            }

            case { LeafValue: { } value }:
                // 叶子定点写：与既有口径一致（无内容指纹去重），恒发。
                return new PendingSection(s, new { op = "leaf", name = s.Name, value }, string.Empty, 0L, string.Empty);

            case { Html: { } html }:
            {
                var slot = RegionSlot(s.Name);
                var fp = _fingerprint(html);
                if (!Admit(slot, fp, s, unchanged, out var version)) return null;

                // HTML 节无旧键集合（解析成本 > 收益）⇒ 只按节点阈值判定 Bulk（比率判据对树节生效）。
                var mode = BulkThresholdPolicy.Decide(
                    _options.BulkReplaceEnabled, s.Nodes, 0, _options.BulkNodeThreshold, _options.BulkRatioThreshold);
                object op = mode == SectionWriteMode.BulkReplace
                    ? new { op = "bulk", name = s.Name, html }
                    : new { op = "html", name = s.Name, html };
                return new PendingSection(s with { Mode = mode }, op, slot, version, fp);
            }

            default:
                return null;
        }
    }

    private bool Admit(string slot, string fingerprint, SectionPlan s, List<string> unchanged, out long version)
    {
        if (_ledger.TryAdmit(slot, fingerprint, out version)) return true;
        _metrics.OnCoalesced();
        _metrics.OnSection(s.Mode, applied: false);
        unchanged.Add(s.Name);
        _sectionTelemetry.OnSection(new SectionOutcome(s.Id, s.Mode, s.Nodes, s.Bytes, 0, "skipped", "unchanged"));
        return false;
    }
}

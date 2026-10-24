#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace UiBackend.Html;

public sealed partial class HtmlBackendRuntime
{
    /// <summary>单节准入与载荷打包（去重经唯一账本 <see cref="WriteLedger"/>；Bulk 由阈值策略判定）。</summary>
    private PendingSection? Prepare(SectionPlan s, List<string> unchanged)
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
                if (!Admit(slot, fp, s, unchanged, out var version)) return null;

                var keys = BulkThresholdPolicy.CollectKeys(tree);
                _sectionKeys.TryGetValue(slot, out var oldKeys);
                var ratio = BulkThresholdPolicy.ChangeRatio(oldKeys, keys);
                var mode = BulkThresholdPolicy.Decide(
                    _options.BulkReplaceEnabled, s.Nodes, ratio, _options.BulkNodeThreshold, _options.BulkRatioThreshold);
                object op = mode == SectionWriteMode.BulkReplace
                    ? new { op = "bulk", name = s.Name, html = UiNodeHtml.ToHtml(tree) }
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

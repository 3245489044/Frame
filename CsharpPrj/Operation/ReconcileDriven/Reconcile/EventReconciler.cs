using System.Text.Json;
using Modules.Sequencing;
using Operation.ReconcileDriven.Contracts;
using Operation.ReconcileDriven.Trace;
using TConfig;

namespace Operation.ReconcileDriven.Reconcile;

/// <summary>事件对账器端口：输入「声明契约 + 实际信号账」，输出「四判裁决」。</summary>
public interface IEventReconciler
{
    ReconcileVerdict Reconcile(ReconcileContract contract, SignalTrace trace);
}

/// <summary>
/// 事件对账器（唯一实现）：四判 —— 完整性 / 顺序 / 终止 / 归属。
/// <para>单实现约束：信号匹配逻辑只有一份；值层 payload 断言为可选降级，不作对账主干，也不由 AssertSystem 反向复用。</para>
/// </summary>
public sealed class EventReconciler : IEventReconciler
{
    public ReconcileVerdict Reconcile(ReconcileContract contract, SignalTrace trace)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(trace);

        var missing = new List<string>();
        var outOfOrder = new List<string>();
        var scopeMismatch = new List<string>();
        var terminalMissing = false;

        // 一次分组：后续按 kind 检索不再线性扫全账（O(kinds + hits)）。
        var byKind = new Dictionary<string, List<SignalRecord>>(StringComparer.Ordinal);
        foreach (var record in trace.Records)
        {
            if (!byKind.TryGetValue(record.EventKind, out var list))
                byKind[record.EventKind] = list = new List<SignalRecord>();
            list.Add(record);
        }

        // 按声明序命中的记录（顺序判据用 EventSeq 校验，不用数组下标）。
        var orderedHits = new List<SignalRecord>();

        foreach (var exp in contract.Signals)
        {
            var kind = exp.Signal!;
            byKind.TryGetValue(kind, out var candidates);

            // 归属优先：声明了 scope 时须存在「同 kind 且同归属」的记录。
            SignalRecord? hit;
            if (string.IsNullOrWhiteSpace(exp.Scope))
            {
                hit = candidates is { Count: > 0 } ? candidates[0] : null;
            }
            else
            {
                hit = candidates?.FirstOrDefault(r => ScopeMatches(r, exp));

                // 信号已观测到但归属别处 ⇒ 只判归属串台（R4），不重复计为缺失（R1）。
                if (hit is null && candidates is { Count: > 0 })
                {
                    scopeMismatch.Add($"{kind}@{exp.Scope}");
                    continue;
                }
            }

            if (hit is null)
            {
                missing.Add(kind);
                if (exp.Terminal) terminalMissing = true;
                continue;
            }

            orderedHits.Add(hit);

            // 终止信号存在性由「完整性 + Terminal 标记」共同承担（缺失即已在上面记 missing/terminalMissing）。
        }

        if (contract.IsStrictOrder && orderedHits.Count > 1)
        {
            // 序判据单源：命中记录在 trace 中的 EventSeq 必须单调不减；
            // 违序定位交给 Modules.Sequencing（首个违序下标 + 基准）。
            if (MonotonicSequenceValidator.TryFindFirstViolation(
                    orderedHits, r => r.EventSeq, strict: false, out var violation))
            {
                outOfOrder.Add(orderedHits[violation.Index].EventKind);
            }
        }

        var passed = missing.Count == 0
                     && outOfOrder.Count == 0
                     && !terminalMissing
                     && scopeMismatch.Count == 0;

        return new ReconcileVerdict(passed, missing, outOfOrder, terminalMissing, scopeMismatch);
    }

    /// <summary>
    /// 归属匹配（G-3 键名映射）：
    /// ① ScopeId 精确匹配；② 声明了 <see cref="SignalExpectation.ScopeKey"/> 时，只比对载荷中该字段的值。
    /// 未声明键名且 ScopeId 不匹配 ⇒ 不匹配（不做载荷子串兜底，避免 "user:1" 命中 "user:10" 的假通过）。
    /// </summary>
    private static bool ScopeMatches(SignalRecord r, SignalExpectation exp)
    {
        var scope = exp.Scope!;
        if (string.Equals(r.ScopeId, scope, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(r.PayloadJson) || string.IsNullOrWhiteSpace(exp.ScopeKey))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(r.PayloadJson!);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(exp.ScopeKey!, out var v)
                   && v.ValueKind == JsonValueKind.String
                   && string.Equals(v.GetString(), scope, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

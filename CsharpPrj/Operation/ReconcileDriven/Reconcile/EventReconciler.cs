using System.Text.Json;
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
        var strictOrder = contract.IsStrictOrder;
        var lastIndex = -1;

        foreach (var exp in contract.Signals)
        {
            var kind = exp.Signal!;

            // 归属优先：声明了 scope 时须存在「同 kind 且同归属」的记录。
            SignalRecord? hit;
            if (string.IsNullOrWhiteSpace(exp.Scope))
            {
                hit = trace.First(kind);
            }
            else
            {
                hit = trace.Records.FirstOrDefault(r =>
                    string.Equals(r.EventKind, kind, StringComparison.Ordinal) && ScopeMatches(r, exp));

                // 信号已观测到但归属别处 ⇒ 只判归属串台（R4），不重复计为缺失（R1）。
                if (hit is null && trace.Contains(kind))
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

            if (strictOrder)
            {
                var idx = IndexOfRecord(trace, hit);
                if (idx < lastIndex) outOfOrder.Add(kind);
                else lastIndex = idx;
            }

            // 终止信号存在性由「完整性 + Terminal 标记」共同承担（缺失即已在上面记 missing/terminalMissing）。
        }

        var passed = missing.Count == 0
                     && outOfOrder.Count == 0
                     && !terminalMissing
                     && scopeMismatch.Count == 0;

        return new ReconcileVerdict(passed, missing, outOfOrder, terminalMissing, scopeMismatch);
    }

    private static int IndexOfRecord(SignalTrace trace, SignalRecord record)
    {
        for (var i = 0; i < trace.Records.Count; i++)
            if (ReferenceEquals(trace.Records[i], record)) return i;
        return -1;
    }

    /// <summary>
    /// 归属匹配（G-3 键名映射）：
    /// ① ScopeId 精确匹配；② 声明了 <see cref="SignalExpectation.ScopeKey"/> 时，只比对载荷中该字段的值；
    /// ③ 未声明键名时退化为载荷子串包含（兼容既有声明）。
    /// </summary>
    private static bool ScopeMatches(SignalRecord r, SignalExpectation exp)
    {
        var scope = exp.Scope!;
        if (string.Equals(r.ScopeId, scope, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(r.PayloadJson))
            return false;

        if (!string.IsNullOrWhiteSpace(exp.ScopeKey))
        {
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

        return r.PayloadJson!.Contains(scope, StringComparison.OrdinalIgnoreCase);
    }
}

using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 目标集规划唯一实现：滤空 → 去自身（实体判等）→ 去重（保序）→ 上限 → 空集策略。
/// 禁排序（输入序即分发优先序）；每个丢弃面必须分类计数（I4）。
/// </summary>
public sealed class KeySetPlanner : IKeySetPlanner
{
    private readonly IKeyClassifier? _classifier;
    private readonly IKeyEquivalence? _equivalence;

    public KeySetPlanner(IKeyClassifier? classifier = null, IKeyEquivalence? equivalence = null)
    {
        _classifier = classifier;
        _equivalence = equivalence;
    }

    public KeySetPlan Plan(KeySetPlanInput input)
    {
        var raw = input.RawMembers ?? Array.Empty<string>();
        var targets = new List<KeyRef>(raw.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var blank = 0;
        var self = 0;
        var duplicate = 0;
        var unparsable = 0;

        foreach (var text in raw)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                blank++;
                continue;
            }

            KeyRef key;
            if (_classifier is not null)
            {
                var classification = _classifier.Classify(text);
                if (classification.Resolution is KeyResolution.Unparsable
                    or KeyResolution.NotDeclared
                    or KeyResolution.Ambiguous
                    or KeyResolution.Empty)
                {
                    unparsable++;
                    continue;
                }

                key = classification.Key ?? KeyRef.Of(input.Declaration.Id, text);
            }
            else
            {
                key = KeyRef.Of(input.Declaration.Id, text);
            }

            if (_equivalence is not null && _equivalence.IsSelf(key, input.Self))
            {
                self++;
                continue;
            }

            if (!seen.Add(text))
            {
                duplicate++;
                continue;
            }

            targets.Add(key);
        }

        var limit = input.AllowFanout ? Math.Max(1, input.MaxTargets) : 1;
        var cap = 0;
        if (targets.Count > limit)
        {
            cap = targets.Count - limit;
            targets.RemoveRange(limit, targets.Count - limit);
        }

        var reason = BuildReason(targets.Count, blank, self, duplicate, cap, unparsable, input);
        return new KeySetPlan(targets, reason, input.EmptyPolicy, blank, self, duplicate, cap, unparsable);
    }

    private static string BuildReason(
        int planned, int blank, int self, int duplicate, int cap, int unparsable, KeySetPlanInput input)
    {
        if (planned == 0)
        {
            if (input.RawMembers.Count == 0)
            {
                return KeyReasonCodes.SetEmpty;
            }

            if (self > 0 && blank == 0 && duplicate == 0 && unparsable == 0)
            {
                return KeyReasonCodes.SetSelfOnly;
            }

            return KeyReasonCodes.SetEmpty;
        }

        var fanout = input.AllowFanout ? $"fanout:cap={Math.Max(1, input.MaxTargets)}" : "single-target:mode-declared";
        return cap > 0 ? $"{fanout};{KeyReasonCodes.SetCap}" : fanout;
    }
}

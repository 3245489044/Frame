using Modules.Declaration;

namespace Modules.Wiring.Diagnostics;

/// <summary>
/// 校验报告（全量清单 + Report/Throw 姿态）：姿态语义复用 <see cref="ClosureStrictness"/> 单源。
/// </summary>
public sealed record WiringReport(IReadOnlyList<WiringViolation> Violations)
{
    public static WiringReport Empty { get; } = new(Array.Empty<WiringViolation>());

    public bool IsClosed => Violations.Count == 0;

    public int Count => Violations.Count;

    public IReadOnlyList<WiringViolation> ByCode(string code)
    {
        var hits = new List<WiringViolation>();
        foreach (var violation in Violations)
        {
            if (string.Equals(violation.Code, code, StringComparison.Ordinal)) hits.Add(violation);
        }

        return hits;
    }

    public bool Has(string code) => ByCode(code).Count > 0;

    /// <summary>姿态裁决：Throw ⇒ 有违规即抛（fail-fast，禁降级为「测试失败」）。</summary>
    public IReadOnlyList<WiringViolation> Enforce(ClosureStrictness strictness, string scope)
    {
        if (strictness == ClosureStrictness.Throw && Violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"接线未闭合（{scope}）：{string.Join(" | ", Violations.Select(v => v.Describe()))}");
        }

        return Violations;
    }

    public string Summary => IsClosed
        ? "closed(0 violations)"
        : $"{Violations.Count} violations: {string.Join(",", Violations.Select(v => v.Code).Distinct(StringComparer.Ordinal))}";

    public static WiringReport Of(IEnumerable<WiringViolation> violations)
        => new(violations as IReadOnlyList<WiringViolation> ?? violations.ToArray());

    public static WiringReport Merge(params WiringReport[] reports)
    {
        var all = new List<WiringViolation>();
        foreach (var report in reports)
        {
            if (report is not null) all.AddRange(report.Violations);
        }

        return new WiringReport(all);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 绑定校验的**一次聚合报告**（纯数据）：校验层只产报告，报告出口由宿主决定（Warn 一条 / Debug 明细）。
/// <para>
/// 存在理由（ANTI-ATTENTION-SPREAD）：逐条违规直接写 WARN 会把「份数 × 视图 × 拍数」放大成告警风暴，
/// 掩盖真正的结构缺陷。聚合后「一拍一条」可读，明细降级为诊断。
/// </para>
/// </summary>
/// <param name="Scope">校验作用域（如 <c>RenderView:&lt;task&gt;</c>）。</param>
/// <param name="Total">违规条数。</param>
/// <param name="ByRule">按规则计数（键 = 规则名，稳定序）。</param>
/// <param name="BySource">按来源计数（Ui / Llm）。</param>
/// <param name="DistinctKeys">去重后的锚点键（升序，便于取证比对）。</param>
public sealed record BindingReport(
    string Scope,
    int Total,
    IReadOnlyDictionary<string, int> ByRule,
    IReadOnlyDictionary<string, int> BySource,
    IReadOnlyList<string> DistinctKeys)
{
    /// <summary>由违规序列装配报告（空集亦可，<see cref="Total"/> = 0）。</summary>
    /// <param name="scope">校验作用域。</param>
    /// <param name="violations">违规序列（可空）。</param>
    public static BindingReport From(string scope, IReadOnlyList<BindingViolation>? violations)
    {
        var all = violations ?? Array.Empty<BindingViolation>();
        var byRule = all
            .GroupBy(v => v.Rule.ToString(), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var bySource = all
            .GroupBy(v => v.Source, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var keys = all
            .Select(v => v.AnchorKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new BindingReport(scope, all.Count, byRule, bySource, keys);
    }

    /// <summary>规则计数单行（<c>R4_KeyUnique=52;R6_ZeroExtra=2</c>）；空集 ⇒ <c>-</c>。</summary>
    public string DescribeRules()
        => ByRule.Count == 0 ? "-" : string.Join(";", ByRule.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>来源计数清单（<c>Ui=52;Llm=52</c>）；空集 ⇒ <c>-</c>。</summary>
    public string DescribeSources()
        => BySource.Count == 0 ? "-" : string.Join(";", BySource.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>取证单行（明细降级为诊断时使用）。</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("scope=").Append(Scope)
            .Append(" total=").Append(Total)
            .Append(" rules=").Append(DescribeRules())
            .Append(" sources=").Append(DescribeSources());
        return builder.ToString();
    }
}

/// <summary>
/// 绑定诊断出口（双档位）：<see cref="Warn"/> **每拍一条**汇总，<see cref="Debug"/> 逐条明细。
/// <para>宿主注入（日志 / 测试捕获）；校验层不感知具体日志设施。</para>
/// </summary>
public interface IBindingDiagnosticSink
{
    /// <summary>汇总（一拍一条，Warn 档）。</summary>
    void Warn(BindingReport report);

    /// <summary>逐条明细（Debug 档）。</summary>
    void Debug(BindingViolation violation);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Conversion.Packs;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 域包契约：把「身份 + 诊断码段 + facet provider + 闸A 规则」绑为一个可注册单元。
/// <para>
/// 扩展内核 <see cref="IDomainPack{TModel}"/>（保留其 <see cref="GateRules"/> 语义），追加
/// <see cref="FacetProviders"/> —— 使新增领域面 = 新增一个包，零核心改动（消除 H1–H7）。
/// </para>
/// </summary>
public interface IUsharpDomainPack
{
    /// <summary>包标识（如 <c>usharp</c> / <c>agent.tool</c>）。</summary>
    string Id { get; }

    /// <summary>本包诊断码段（唯一真源声明，如 <c>US</c>）。</summary>
    IReadOnlyList<string> DiagnosticCodePrefixes { get; }

    /// <summary>facet 投影器（声明 → 领域视图；序 = 包声明序）。</summary>
    IReadOnlyList<IUsharpFacetProvider> FacetProviders { get; }

    /// <summary>闸A 规则集（执行序 = 声明序）。</summary>
    IReadOnlyList<IGateRule<UsharpGateModel>> GateRules { get; }
}

/// <summary>
/// 多包组合（消除 H6）：把多个 <see cref="IUsharpDomainPack"/> 的规则/facet 按**包声明序**拼接。
/// <para>规则执行序 = <c>packs.SelectMany(p =&gt; p.GateRules)</c> ⇒ 诊断序稳定且可预测。</para>
/// </summary>
public sealed class CompositeDomainPack : IUsharpDomainPack
{
    private readonly IReadOnlyList<IUsharpDomainPack> _packs;

    public CompositeDomainPack(params IUsharpDomainPack[] packs)
        => _packs = packs ?? Array.Empty<IUsharpDomainPack>();

    /// <summary>子包集合（声明序）。</summary>
    public IReadOnlyList<IUsharpDomainPack> Packs => _packs;

    public string Id => "composite:" + string.Join("+", _packs.Select(p => p.Id));

    public IReadOnlyList<string> DiagnosticCodePrefixes
        => _packs.SelectMany(p => p.DiagnosticCodePrefixes).ToArray();

    public IReadOnlyList<IUsharpFacetProvider> FacetProviders
        => _packs.SelectMany(p => p.FacetProviders).ToArray();

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules
        => _packs.SelectMany(p => p.GateRules).ToArray();
}

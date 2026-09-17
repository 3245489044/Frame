using BasicMethod.Declarative.Ledger;
using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// C1–C13 判据的规则化包装：编号 + 求值入口，机制宿主 = 框架 <see cref="LedgerConsistency"/>。
/// <para>
/// 本批只搬宿主、不动判据逻辑（判据体仍在 <see cref="StandardConsistency"/> /
/// <see cref="StandardImplConsistency"/>，逐字未改）。
/// </para>
/// </summary>
internal static class StandardRuleSet
{
    /// <summary>全部判据（顺序 = 迁移前执行顺序：C1–C6 契约面，C7–C14 实现面）。</summary>
    public static IReadOnlyList<ILedgerRule<IStandardCatalog>> All(StandardConsistencyOptions options) =>
    [
        new KeyUniquenessRule(),
        new EmitsRule(),
        new CarriesRule(),
        new CrossCutFieldsRule(options),
        new ProducersRule(options),
        new VersionMonotonicRule(options),
        new ImplKindRule(),
        new ComposedRule(),
        new ScriptedRule(),
        new DeliverRule(),
        new DepsRule(),
        new ProjectionRule(),
        new ProductFieldCoverageRule(),
        new ValueSourceRule(),
    ];
}

/// <summary>C1 键唯一（含原文层重复键扫描）。</summary>
internal sealed class KeyUniquenessRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C1";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckKeyUniqueness(catalog, violations);
}

/// <summary>C2 <c>ports.emits ⊆ events</c>。</summary>
internal sealed class EmitsRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C2";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckEmits(catalog, EventKeys(catalog), violations);

    private static HashSet<string> EventKeys(IStandardCatalog catalog)
        => new(catalog.Events.Select(e => e.Key), StringComparer.Ordinal);
}

/// <summary>C3 <c>events.carries ⊆ data</c>。</summary>
internal sealed class CarriesRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C3";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckCarries(catalog, StandardConsistency.DataKeysNormalized(catalog), violations);
}

/// <summary>C4 跨切字段 ⊆ data。</summary>
internal sealed class CrossCutFieldsRule(StandardConsistencyOptions options) : ILedgerRule<IStandardCatalog>
{
    public string Code => "C4";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckCrossCutFields(catalog, StandardConsistency.DataKeysNormalized(catalog), options, violations);
}

/// <summary>C5 <c>data.producer ⊆ ports ∪ 白名单</c>。</summary>
internal sealed class ProducersRule(StandardConsistencyOptions options) : ILedgerRule<IStandardCatalog>
{
    public string Code => "C5";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckProducers(catalog, StandardConsistency.PortKeys(catalog), options, violations);
}

/// <summary>C6 版本单调（与上一快照比对）。</summary>
internal sealed class VersionMonotonicRule(StandardConsistencyOptions options) : ILedgerRule<IStandardCatalog>
{
    public string Code => "C6";

    public void Check(IStandardCatalog catalog, List<string> violations)
        => StandardConsistency.CheckVersionMonotonic(catalog, options, violations);
}

/// <summary>C7 <c>impl.kind</c> 必随字段。</summary>
internal sealed class ImplKindRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C7";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC7(port, port.Impl, violations);
    }
}

/// <summary>C8 取值来源 ↔ 伴生键。</summary>
internal sealed class ValueSourceRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C8";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC8ValueSources(port, port.Impl, violations);
    }
}

/// <summary>C9 组合族跨项引用。</summary>
internal sealed class ComposedRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C9";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC9Composed(port, port.Impl, violations);
    }
}

/// <summary>C10 脚本族结构约束。</summary>
internal sealed class ScriptedRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C10";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC10Scripted(port, port.Impl, violations);
    }
}

/// <summary>C11 载体声明约束。</summary>
internal sealed class DeliverRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C11";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC11Deliver(port, port.Impl, violations);
    }
}

/// <summary>C12 依赖声明约束。</summary>
internal sealed class DepsRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C12";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC12Deps(port, port.Impl, violations);
    }
}

/// <summary>C13 产物字段 ⊆ 声明字段（判据单源唯一实现）。</summary>
internal sealed class ProjectionRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C13";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports) StandardImplConsistency.CheckC13Projection(port, port.Impl, violations);
    }
}

/// <summary>C14 断言面覆盖：<c>out.fields ⊆ impl.productFields</c>（D8-a：双源保留、对拍判据升为装载期判据）。</summary>
internal sealed class ProductFieldCoverageRule : ILedgerRule<IStandardCatalog>
{
    public string Code => "C14";

    public void Check(IStandardCatalog catalog, List<string> violations)
    {
        foreach (var port in catalog.Ports)
            StandardImplConsistency.CheckC14ProductFieldCoverage(port, port.Impl, violations);
    }
}

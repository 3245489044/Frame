using Modules.Wiring.Abstractions;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

/// <summary>收集产物：目录（只读投影）+ 声明级结构码。</summary>
public sealed record WiringCollection(
    WiringCatalog Catalog,
    IReadOnlyList<WiringViolation> Violations)
{
    public bool IsClean => Violations.Count == 0;
}

/// <summary>
/// Collect：声明来源 → <see cref="WiringCatalog"/> + 声明级校验（唯一投影入口）。
/// <para>实现侧事实只参与 D7（<see cref="WiringValidator"/>），不在收集期改写声明。</para>
/// </summary>
public static class WiringCollector
{
    public static WiringCollection Collect(
        IWiringDeclarationSource source,
        IWiringImplementationCatalog? implementations = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var catalog = WiringCatalogFactory.FromSource(source);
        var violations = new List<WiringViolation>(WiringValidator.VerifyDeclaration(catalog));

        if (implementations is not null)
        {
            foreach (var box in catalog.Boxes)
            {
                // 未登记实现的盒不参与静态闭合（灰度口径：先登记再收紧），但可见化登记事实。
                if (!implementations.IsRegistered(box.Impl))
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WirePortDrift, box.Id,
                        "实现未登记（D7 跳过：先登记再收紧）",
                        "impl=" + box.Impl));
                }
            }
        }

        return new WiringCollection(catalog, violations);
    }
}

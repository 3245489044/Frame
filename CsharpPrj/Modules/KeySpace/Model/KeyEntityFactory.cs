using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.KeySpace.Model;

/// <summary>
/// 实体构造<b>唯一宿主</b>：一组外部称呼（别名）→ 一个 <see cref="KeyEntity"/>。
/// <para>
/// 收敛对象：<c>Agent.Session.Keys.SessionKeySpace.EntityOf</c> 与 <c>TargetSetPlanner.Plan</c> 此前各自内联
/// 构造实体（改派生规则时两处必须同步，无人声明谁是权威）。本件只做<b>机制</b>：
/// 去空白 → 保序去重 → Id 取首个 → 逐称呼构造 <see cref="KeyRef"/>。
/// </para>
/// <para>
/// 族与域的<b>策略</b>仍由产品侧给出（<c>familyId</c>/<c>domain</c> 实参）：声明面无法决定「这组别名属于哪个族」，
/// 从族表里猜会把隐式规则塞进框架（Modules.KeySpace 零域词）。
/// </para>
/// </summary>
public static class KeyEntityFactory
{
    /// <summary>三称呼别名 → 实体（去自身判等的唯一输入形态）。空/空白别名不参与；全部为空 ⇒ 空实体。</summary>
    /// <param name="aliases">外部称呼序列（序即优先序：首个非空 = 实体 Id）。</param>
    /// <param name="familyId">别名所属键族（产品侧策略）。</param>
    /// <param name="domain">别名所属域（产品侧策略）。</param>
    public static KeyEntity FromAliases(
        IEnumerable<string?> aliases,
        string familyId,
        KeyDomainId domain)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        var values = aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Length == 0)
        {
            return new KeyEntity(string.Empty, Array.Empty<KeyRef>());
        }

        var refs = values
            .Select(value => KeyRef.Of(familyId, value, domain))
            .ToArray();

        return new KeyEntity(values[0], refs);
    }
}

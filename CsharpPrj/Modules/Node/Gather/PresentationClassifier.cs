namespace Modules.Node.Gather;

/// <summary>
/// **呈现类**注入（框架级、零领域词）：按声明 <c>kind-shaping.presentation=</c> 把「帧类别 → 呈现类」
/// 写进帧字段袋（键 = <see cref="FieldName"/>），供选择表达式 <c>presentation hasany (bubble)</c> 消费。
/// <para>
/// 存在理由（SingleImplementation）：此前「哪条算气泡」由 kind 集合（selection）+ 位置启发（终答提升）+
/// role/kind 规则三处各自表达 ⇒ 同一轮次在不同路径可判成 1 或 2 条。本类把「类别 → 呈现类」收敛为
/// **一处声明投影**；消费方只读字段，不再按 kind 写第二张表。
/// </para>
/// </summary>
public static class PresentationClassifier
{
    /// <summary>呈现类字段名（帧字段袋键；与声明面 <c>presentation=</c> 同形）。</summary>
    public const string FieldName = "presentation";

    /// <summary>未声明呈现类的帧一律归此值（可见、可过滤，不伪装成气泡）。</summary>
    public const string Unclassified = "plain";

    /// <summary>
    /// 由聚拢声明构造字段袋分类器：未声明 <c>presentation=</c> 的 kind 归 <see cref="Unclassified"/>；
    /// 已带同值字段 ⇒ 原样返回（零分配）。
    /// </summary>
    /// <param name="spec">聚拢声明（kind → 塑形，含通配 <c>*</c>）。</param>
    public static Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> FromSpec(
        NodeGatherSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return fields =>
        {
            if (fields.TryGetValue(FieldName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                return fields;
            }

            var value = spec.TryGetShaping(spec.KindOf(fields), out var shaping) &&
                        !string.IsNullOrWhiteSpace(shaping.Presentation)
                ? shaping.Presentation!
                : Unclassified;

            return new Dictionary<string, string>(fields, StringComparer.Ordinal) { [FieldName] = value };
        };
    }
}

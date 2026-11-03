namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 关系查询轴取值（单源；禁在查询构造/声明编译/门禁各处手写字面量）。
/// </summary>
public static class RelationQueryVocabulary
{
    /// <summary>归一化 = 原样（身份键）。</summary>
    public const string NormalizeIdentity = "identity";

    /// <summary>归一化 = 节点键口径（跨库链去重用）。</summary>
    public const string NormalizeNodeKey = "node-key";

    /// <summary>排序 = 键序（Ordinal 升序；确定性输出）。</summary>
    public const string OrderKeyOrdinal = "key-ordinal";
}

/// <summary>
/// 关系查询规格（F4 契约壳）—— <c>[Derive]</c> 编译产物，声明一次、查询端口统一求值。
/// <para>
/// 纪律：① 只读（无写字段）；② 有界（<see cref="MaxDepth"/>/<see cref="MaxNodes"/> 强制预算，
/// 禁「取全量后再过滤」）；③ 空集必须带空因（结果形态 = <see cref="RelationQueryResult"/>）。
/// </para>
/// 权威: Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations 关系内核（唯一读入口）。
/// </summary>
/// <param name="From">起点实体键（非空；缺失即不可判定）。</param>
/// <param name="To">终点实体键（null = 不限终点）。</param>
/// <param name="Label">边标签（null = 不限标签；非空 = 只走该关系）。</param>
/// <param name="Via">途经边链（按序；null = 由 <see cref="Label"/> 决定）。</param>
/// <param name="MaxDepth">最大遍历深度（&gt;= 1；根 = 1）。</param>
/// <param name="MaxNodes">最大节点数（0 = 用声明面预算；&gt; 0 = 显式上界）。</param>
/// <param name="Normalize">归一化口径（见 <see cref="RelationQueryVocabulary"/>）。</param>
/// <param name="Order">输出序（见 <see cref="RelationQueryVocabulary"/>）。</param>
public sealed record RelationQuerySpec(
    string From,
    string? To = null,
    string? Label = null,
    IReadOnlyList<string>? Via = null,
    int MaxDepth = 1,
    int MaxNodes = 0,
    string Normalize = RelationQueryVocabulary.NormalizeIdentity,
    string Order = RelationQueryVocabulary.OrderKeyOrdinal);

/// <summary>
/// 规格化关系查询端口（F4 唯一读入口的扩展面）：<see cref="RelationQuerySpec"/> → 键集 + 空因/截断因。
/// <para>
/// 纪律：① 有界（<c>MaxDepth</c>/<c>MaxNodes</c> 强制预算，截断必须可见）；② 空集必带空因；
/// ③ 与既有 <see cref="IRelationQueryPort"/> 并存（后者按 operation/derive 触发，本端口按规格求值）；
/// ④ 禁自 join：实现只经 <c>IResourceReferenceStore</c> 逐跳读取。
/// </para>
/// </summary>
public interface IRelationSpecQueryPort
{
    /// <summary>按规格查询（<paramref name="spec"/> 的 <c>From</c> 非空；未声明标签 ⇒ 空集 + 空因）。</summary>
    Task<RelationQueryResult> QueryAsync(
        RelationQuerySpec spec, RelationScope scope, CancellationToken ct = default);
}

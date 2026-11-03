namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 边产品字段名（跨语言交换的唯一拼写源；禁在写入/投影/查询处手写字面量）。
/// </summary>
public static class EdgeProductFields
{
    /// <summary>边规范键（<c>from|label|to</c>；与 <see cref="DataDependencyEdge.EdgeId"/> 同形态）。</summary>
    public const string EdgeId = "edge_id";

    /// <summary>源端点键。</summary>
    public const string FromId = "from_id";

    /// <summary>目标端点键。</summary>
    public const string ToId = "to_id";

    /// <summary>边标签（同一端点对上的关系区分键）。</summary>
    public const string Label = "label";

    /// <summary>断言强度（<see cref="DependencyAssertions"/> 线上词形）。</summary>
    public const string Assertion = "assertion";

    /// <summary>创建序号（图内单调；与 <see cref="TraceStamp.Seq"/> 同源但不含时刻）。</summary>
    public const string CreatedSeq = "created_seq";

    /// <summary>墓碑标记（删除不删行；复活策略由声明面决定）。</summary>
    public const string Tombstone = "tombstone";

    /// <summary>写入该边的流程标识（追溯键）。</summary>
    public const string OriginFlow = "origin_flow";

    /// <summary>写入该边的步骤键（追溯键）。</summary>
    public const string OriginStep = "origin_step";
}

/// <summary>
/// 边产品契约（DP2 的声明面载体）：边作为一等数据产品，携带 Id / 断言 / 墓碑 / 来源，
/// 因此可被失效传播、追溯与对账；<b>不是</b>「写进库的一行」。
/// <para>
/// 纪律：① 零领域词汇（边标签由声明源提供，本记录只承载形态）；② 幂等键禁含易变分量
/// （时间/序号），故恒为 <c>from_id|label|to_id</c>；③ 纯值类型，无 IO / 无状态。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §3.3/§3.4。
/// </summary>
/// <param name="ProductId">产品规范键（层内唯一）。</param>
/// <param name="LevelId">层级轴引用（与层级 schema 的层 Id 一致）。</param>
/// <param name="Label">边标签（同一端点对上的关系区分键；非空）。</param>
/// <param name="OutFields">出字段契约（默认 = <see cref="DefaultOutFields"/>）。</param>
/// <param name="IdempotencyKey">幂等键表达式（结构事实；默认 <see cref="DefaultIdempotencyKey"/>）。</param>
/// <param name="Version">产品版本（单调不减）。</param>
public sealed record EdgeProductSpec(
    string ProductId,
    string LevelId,
    string Label,
    IReadOnlyList<FieldSpec> OutFields,
    string IdempotencyKey = EdgeProductSpec.DefaultIdempotencyKey,
    int Version = 1)
{
    /// <summary>默认幂等键（三段端点+标签；不含易变分量）。</summary>
    public const string DefaultIdempotencyKey = "from_id|label|to_id";

    /// <summary>
    /// 默认出字段契约（9 项）：边身份 + 两端 + 标签 + 断言 + 创建序 + 墓碑 + 来源流程/步骤。
    /// </summary>
    public static IReadOnlyList<FieldSpec> DefaultOutFields { get; } = new[]
    {
        new FieldSpec(EdgeProductFields.EdgeId, "string"),
        new FieldSpec(EdgeProductFields.FromId, "string"),
        new FieldSpec(EdgeProductFields.ToId, "string"),
        new FieldSpec(EdgeProductFields.Label, "string"),
        new FieldSpec(EdgeProductFields.Assertion, "string"),
        new FieldSpec(EdgeProductFields.CreatedSeq, "int"),
        new FieldSpec(EdgeProductFields.Tombstone, "bool"),
        new FieldSpec(EdgeProductFields.OriginFlow, "string"),
        new FieldSpec(EdgeProductFields.OriginStep, "string"),
    };

    /// <summary>按默认出字段契约构造（只声明偏离项时才用完整构造）。</summary>
    public static EdgeProductSpec Create(string productId, string levelId, string label)
        => new(productId, levelId, label, DefaultOutFields);

    /// <summary>
    /// 边规范键（<c>from|label|to</c>）——纯计算，与声明态/运行态取值域无关。
    /// 空端点或空标签 ⇒ 抛（禁产出不可复算的边键）。
    /// </summary>
    public string EdgeId(string fromId, string toId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);

        return string.Join('|', fromId, Label, toId);
    }
}

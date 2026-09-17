namespace DataCenter.Protocol.Standards;

/// <summary>
/// 表达式子槽的下钻形态（<see cref="ExprStandard"/> 的成员 → 子节点取法）。
/// <para>形态判定由本表承载，禁在遍历/判据里另按注释推断：新增成员必须在本表登记（见 <see cref="PortImplVocabulary.ExprSlots"/>）。</para>
/// </summary>
public enum ExprSlotKind
{
    /// <summary>非子表达式成员（标量 / 布尔 / 字符串）：不参与下钻，但参与算子组判定。</summary>
    None,

    /// <summary>单值子表达式（<c>ExprStandard?</c>）。</summary>
    One,

    /// <summary>子表达式序列（<c>IReadOnlyList&lt;ExprStandard&gt;?</c>；定长二元比较亦归此类）。</summary>
    Many,

    /// <summary>拼接段（<c>Parts</c>：段表达式 + 可选段门控）。</summary>
    Parts,

    /// <summary>取值来源（<c>Source</c> / <c>FoldInit</c>）。</summary>
    Source,

    /// <summary>调用算子（<c>Invoke</c>）。</summary>
    Invoke,

    /// <summary>折叠步（<c>FoldStep</c>：链式调用，其实参为取值来源）。</summary>
    Chain,
}

/// <summary>
/// 表达式成员子槽：成员名 + 算子组 + 下钻形态。
/// <para><paramref name="Member"/> 与 <see cref="ExprStandard"/> 的属性名严格同名（反射可对拍）；
/// <paramref name="OperatorGroup"/> 非空 = 同组成员共属一个算子（C10「算子组恰一」的取值域），null = 各自独立成组。</para>
/// </summary>
public readonly record struct ExprSlot(string Member, string? OperatorGroup, ExprSlotKind Kind)
{
    /// <summary>是否为子表达式槽（需下钻）。</summary>
    public bool IsChild => Kind != ExprSlotKind.None;
}

public static partial class PortImplVocabulary
{
    /// <summary>
    /// 表达式成员子槽表（成员序 = <see cref="ExprStandard"/> 声明序）：遍历下钻（下钻形态）与算子组判定（分组）的共同单源。
    /// <para>纪律：<see cref="ExprStandard"/> 增删成员必须同步本表——`DataCenter.Standards.Tests` 以反射逐项对拍（表 ↔ 属性名集合全等）。</para>
    /// </summary>
    public static readonly IReadOnlyList<ExprSlot> ExprSlots =
    [
        new("Source", null, ExprSlotKind.Source),
        new("Path", "collect", ExprSlotKind.None),
        new("Count", null, ExprSlotKind.One),
        new("NotNull", null, ExprSlotKind.One),
        new("AsText", null, ExprSlotKind.One),
        new("TypeName", null, ExprSlotKind.One),
        new("Message", null, ExprSlotKind.One),
        new("Thrown", null, ExprSlotKind.One),
        new("Concat", "concat", ExprSlotKind.Many),
        new("Separator", "concat", ExprSlotKind.None),
        new("Contains", null, ExprSlotKind.One),
        new("Of", null, ExprSlotKind.One),
        new("All", "quantifier", ExprSlotKind.One),
        new("Any", "quantifier", ExprSlotKind.One),
        new("AllIn", "quantifier", ExprSlotKind.One),
        new("Call", "quantifier", ExprSlotKind.None),
        new("CallTarget", "quantifier", ExprSlotKind.None),
        new("Spread", null, ExprSlotKind.One),
        new("Except", null, ExprSlotKind.None),
        new("CountEqual", null, ExprSlotKind.Many),
        new("Collect", "collect", ExprSlotKind.None),
        new("CollectText", "collect", ExprSlotKind.None),
        new("SameAsLast", "collect", ExprSlotKind.None),
        new("IsNull", null, ExprSlotKind.One),
        new("Not", null, ExprSlotKind.One),
        new("Eq", null, ExprSlotKind.Many),
        new("Ne", null, ExprSlotKind.Many),
        new("Gt", null, ExprSlotKind.Many),
        new("Ge", null, ExprSlotKind.Many),
        new("Lt", null, ExprSlotKind.Many),
        new("Le", null, ExprSlotKind.Many),
        new("AllOf", "junction", ExprSlotKind.Many),
        new("AnyOf", "junction", ExprSlotKind.Many),
        new("Invoke", null, ExprSlotKind.Invoke),
        new("First", "first", ExprSlotKind.One),
        new("FirstWhere", "first", ExprSlotKind.One),
        new("FirstPath", "first", ExprSlotKind.None),
        new("Agg", "aggregate", ExprSlotKind.One),
        new("AggOp", "aggregate", ExprSlotKind.None),
        new("AggPath", "aggregate", ExprSlotKind.None),
        new("Filter", "filter", ExprSlotKind.One),
        new("FilterWhere", "filter", ExprSlotKind.One),
        new("FilterPath", "filter", ExprSlotKind.None),
        new("SameReference", null, ExprSlotKind.Many),
        new("Coalesce", null, ExprSlotKind.Many),
        new("Dict", "dict", ExprSlotKind.One),
        new("DictKey", "dict", ExprSlotKind.One),
        new("DictValue", "dict", ExprSlotKind.One),
        new("DictWhere", "dict", ExprSlotKind.One),
        new("DictOnDuplicate", "dict", ExprSlotKind.None),
        new("Last", "last", ExprSlotKind.One),
        new("LastWhere", "last", ExprSlotKind.One),
        new("LastPath", "last", ExprSlotKind.None),
        new("FirstOrNull", "first", ExprSlotKind.None),
        new("LastOrNull", "last", ExprSlotKind.None),
        new("Parts", "concat", ExprSlotKind.Parts),
        new("Distinct", null, ExprSlotKind.One),
        new("SequenceEqual", null, ExprSlotKind.Many),
        new("Fold", "fold", ExprSlotKind.One),
        new("FoldAs", "fold", ExprSlotKind.None),
        new("FoldInit", "fold", ExprSlotKind.Source),
        new("FoldStep", "fold", ExprSlotKind.Chain),
    ];
}

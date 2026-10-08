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
/// 表达式成员子槽：成员名 + 算子组 + 下钻形态 + 承载的 JSON 键 + 词表。
/// <para><paramref name="Member"/> 与 <see cref="ExprStandard"/> 的属性名严格同名（反射可对拍）；
/// <paramref name="OperatorGroup"/> 非空 = 同组成员共属一个算子（C10「算子组恰一」的取值域），null = 各自独立成组；
/// <paramref name="Key"/> = 本成员在表达式对象中的平铺键，<paramref name="NestedKeys"/> = 其对象形态内读取的子键
/// （结构门对该层为浅校验，故子键须在表达式对象层一并登记）；
/// <paramref name="Vocabulary"/> 非空 = 本键的值域由 <see cref="PortImplVocabulary"/> 某静态成员渲染
/// （结构门 <c>enum</c> 由此派生，见 <c>SchemaEnumSynchronizer</c>）。</para>
/// </summary>
public readonly record struct ExprSlot(
    string Member,
    string? OperatorGroup,
    ExprSlotKind Kind,
    string? Key = null,
    IReadOnlyList<string>? NestedKeys = null,
    string? Vocabulary = null)
{
    /// <summary>是否为子表达式槽（需下钻）。</summary>
    public bool IsChild => Kind != ExprSlotKind.None;

    /// <summary>本成员承载的全部 JSON 键（平铺键 + 宿主对象嵌套子键；序 = 登记序）。</summary>
    public IEnumerable<string> DeclaredKeys
    {
        get
        {
            if (Key is not null) yield return Key;
            if (NestedKeys is null) yield break;

            foreach (var key in NestedKeys) yield return key;
        }
    }
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
        new("Path", "collect", ExprSlotKind.None, "path"),
        new("Count", null, ExprSlotKind.One, "count"),
        new("NotNull", null, ExprSlotKind.One, "notNull"),
        new("AsText", null, ExprSlotKind.One, "text"),
        new("TypeName", null, ExprSlotKind.One, "typeName"),
        new("Message", null, ExprSlotKind.One, "message"),
        new("Thrown", null, ExprSlotKind.One, "thrown"),
        new("Concat", "concat", ExprSlotKind.Many, "concat", ["when", "expr"]),
        new("Separator", "concat", ExprSlotKind.None, "separator"),
        new("Contains", null, ExprSlotKind.One, "contains"),
        new("Of", null, ExprSlotKind.One, "of"),
        new("All", "quantifier", ExprSlotKind.One, "all", ["over"]),
        new("Any", "quantifier", ExprSlotKind.One, "any", ["over"]),
        new("AllIn", "quantifier", ExprSlotKind.One, "allIn"),
        new("Call", "quantifier", ExprSlotKind.None, "call"),
        new("CallTarget", "quantifier", ExprSlotKind.None, "callTarget"),
        new("Spread", null, ExprSlotKind.One, "spread"),
        new("Except", null, ExprSlotKind.None, "except"),
        new("CountEqual", null, ExprSlotKind.Many, "countEqual"),
        new("Collect", "collect", ExprSlotKind.None, "collect"),
        new("CollectText", "collect", ExprSlotKind.None, "text"),
        new("SameAsLast", "collect", ExprSlotKind.None, "sameAsLast"),
        new("IsNull", null, ExprSlotKind.One, "isNull"),
        new("Not", null, ExprSlotKind.One, "not"),
        new("Eq", null, ExprSlotKind.Many, "eq"),
        new("Ne", null, ExprSlotKind.Many, "ne"),
        new("Gt", null, ExprSlotKind.Many, "gt"),
        new("Ge", null, ExprSlotKind.Many, "ge"),
        new("Lt", null, ExprSlotKind.Many, "lt"),
        new("Le", null, ExprSlotKind.Many, "le"),
        new("AllOf", "junction", ExprSlotKind.Many, "all"),
        new("AnyOf", "junction", ExprSlotKind.Many, "any"),
        new("Invoke", null, ExprSlotKind.Invoke, "invoke"),
        new("First", "first", ExprSlotKind.One, "first", ["over", "orNull"]),
        new("FirstWhere", "first", ExprSlotKind.One),
        new("FirstPath", "first", ExprSlotKind.None),
        new("Agg", "aggregate", ExprSlotKind.One, "agg", ["over"]),
        new("AggOp", "aggregate", ExprSlotKind.None, "aggOp", Vocabulary: nameof(PortImplVocabulary.AggOps)),
        new("AggPath", "aggregate", ExprSlotKind.None, "aggPath"),
        new("Filter", "filter", ExprSlotKind.One, "filter", ["over"]),
        new("FilterWhere", "filter", ExprSlotKind.One, "filterWhere"),
        new("FilterPath", "filter", ExprSlotKind.None, "filterPath"),
        new("SameReference", null, ExprSlotKind.Many, "sameReference"),
        new("Coalesce", null, ExprSlotKind.Many, "coalesce"),
        new("Dict", "dict", ExprSlotKind.One, "dict", ["over"]),
        new("DictKey", "dict", ExprSlotKind.One, "dictKey"),
        new("DictValue", "dict", ExprSlotKind.One, "dictValue"),
        new("DictWhere", "dict", ExprSlotKind.One, "dictWhere"),
        new("DictOnDuplicate", "dict", ExprSlotKind.None, "dictOnDuplicate"),
        new("Last", "last", ExprSlotKind.One, "last", ["over", "orNull"]),
        new("LastWhere", "last", ExprSlotKind.One, "lastWhere"),
        new("LastPath", "last", ExprSlotKind.None, "lastPath"),
        new("FirstOrNull", "first", ExprSlotKind.None),
        new("LastOrNull", "last", ExprSlotKind.None, "lastOrNull"),
        new("Parts", "concat", ExprSlotKind.Parts, "concat"),
        new("Distinct", null, ExprSlotKind.One, "distinct"),
        new("SequenceEqual", null, ExprSlotKind.Many, "sequenceEqual"),
        new("Fold", "fold", ExprSlotKind.One, "fold", ["over", "init", "step"]),
        new("FoldAs", "fold", ExprSlotKind.None),
        new("FoldInit", "fold", ExprSlotKind.Source, "init"),
        new("FoldStep", "fold", ExprSlotKind.Chain, "step", ["method", "args", "rebind"]),
        new("Capture", null, ExprSlotKind.None, "capture", ["dep", "member", "at", "index", "path", "orNull"]),
        new("CaptureCount", null, ExprSlotKind.None, "captureCount", ["dep", "member"]),
        new("FakeCount", null, ExprSlotKind.None, "fakeCount", ["dep", "member", "key"]),
        new("Select", "select", ExprSlotKind.One, "select", ["over"]),
        new("SelectExpr", "select", ExprSlotKind.One, "selectExpr"),
        new("SelectWhere", "select", ExprSlotKind.One, "selectWhere"),
        new("Substring", "substring", ExprSlotKind.One, "substring"),
        new("SubstringStart", "substring", ExprSlotKind.One, "start"),
        new("SubstringLength", "substring", ExprSlotKind.One, "length"),
        new("Replace", "replace", ExprSlotKind.One, "replace"),
        new("ReplaceOld", "replace", ExprSlotKind.One, "old"),
        new("ReplaceReplacement", "replace", ExprSlotKind.One, "replacement"),
        new("Trim", null, ExprSlotKind.One, "trim"),
    ];

    /// <summary>
    /// 表达式对象内的**算子键**（由 <see cref="ExprSlots"/> 派生：禁手抄——新增算子只改「声明 + 本表」两处）。
    /// <para>取值域 = 各成员承载的 JSON 键并集（保序去重）；纯数据源键由 <see cref="ValueSourceKeys"/> 承担
    /// （二者并集 = <see cref="ExprKeys"/>，即结构门 <c>$defs/expr/properties</c> 的键集）。</para>
    /// <para>未登记成员（如 <c>FirstWhere</c>/<c>Parts</c>）：读取器只在其宿主对象内取值，无平铺键，故不登记。</para>
    /// <para>分解式补注：<c>all</c>/<c>any</c> 数组形态 = 合取 / 析取，对象形态 = 量词；<c>first</c>/<c>last</c>/
    /// <c>agg</c>/<c>filter</c>/<c>dict</c> 支持「内嵌对象（长形）」与「平铺键（短形）」两种书写。</para>
    /// </summary>
    public static IReadOnlyList<string> ExprOperatorKeys => _exprOperatorKeys ??= DeriveExprOperatorKeys();

    private static IReadOnlyList<string>? _exprOperatorKeys;

    private static IReadOnlyList<string> DeriveExprOperatorKeys()
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in ExprSlots)
            foreach (var key in slot.DeclaredKeys)
                if (seen.Add(key)) keys.Add(key);

        return keys;
    }
}

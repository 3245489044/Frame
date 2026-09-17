namespace DataCenter.Protocol.Standards;

/// <summary>
/// 表达式族键白名单与层名索引（<see cref="PortImplVocabulary"/> 的表达式部分）。
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary>
    /// 表达式对象内的**算子键**（C10「算子恰一个成员」判定的取值域；设计文档 §4.2「表达式算子 40+」据此登记）。
    /// <para>分解式补注：<c>all</c>/<c>any</c> 数组形态 = 合取 / 析取，对象形态 = 量词；<c>first</c>/<c>last</c>/
    /// <c>agg</c>/<c>filter</c>/<c>dict</c> 支持「内嵌对象（长形）」与「平铺键（短形）」两种书写。</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ExprOperatorKeys =
    [
        "count", "notNull", "text", "typeName", "message", "thrown", "concat", "contains", "of", "all", "any",
        "allIn", "call", "callTarget", "spread", "except", "countEqual", "collect", "path", "sameAsLast",
        "isNull", "not", "eq", "ne", "gt", "ge", "lt", "le", "sameReference", "coalesce", "dict", "dictKey",
        "dictValue", "dictWhere", "dictOnDuplicate", "last", "lastWhere", "lastPath", "lastOrNull", "first",
        "orNull", "agg", "aggOp", "aggPath", "filter", "filterWhere", "filterPath", "distinct", "sequenceEqual",
        "fold", "over", "rebind", "method", "args", "step", "init", "when", "expr", "invoke",
    ];

    /// <summary>
    /// 表达式对象允许出现的键 = 取值来源键 ∪ 算子键（读取器 fail-fast 的取值域）。
    /// <para>去重保序：<c>path</c>/<c>distinct</c> 两键在两侧词表中同形（来源侧与算子侧各一次），交集只计一次。</para>
    /// </summary>
    public static IReadOnlyList<string> ExprKeys =>
        ValueSourceKeys.Concat(ExprOperatorKeys).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>给定层的键白名单（未登记层 → null = 该层不做未知键校验）。</summary>
    public static IReadOnlyList<string>? KeysOf(string level) => level switch
    {
        nameof(ImplKeys) => ImplKeys,
        nameof(ArgKeys) => ArgKeys,
        nameof(ValueSourceKeys) => ValueSourceKeys,
        nameof(DepKeys) => DepKeys,
        nameof(CallKeys) => CallKeys,
        nameof(ComposedFieldKeys) => ComposedFieldKeys,
        nameof(StepKeys) => StepKeys,
        nameof(InvokeKeys) => InvokeKeys,
        nameof(NewKeys) => NewKeys,
        nameof(InitKeys) => InitKeys,
        nameof(ChainKeys) => ChainKeys,
        nameof(DelegateKeys) => DelegateKeys,
        nameof(SaveKeys) => SaveKeys,
        nameof(CollectKeys) => CollectKeys,
        nameof(ScopeKeys) => ScopeKeys,
        nameof(FileKeys) => FileKeys,
        nameof(DeliverKeys) => DeliverKeys,
        nameof(CaptureKeys) => CaptureKeys,
        nameof(ScenarioKeys) => ScenarioKeys,
        nameof(ConcatPartKeys) => ConcatPartKeys,
        nameof(FieldExprKeys) => FieldExprKeys,
        nameof(ExprKeys) => ExprKeys,
        _ => null,
    };
}

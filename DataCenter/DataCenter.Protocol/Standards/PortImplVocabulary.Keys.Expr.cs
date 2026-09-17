namespace DataCenter.Protocol.Standards;

/// <summary>
/// 表达式族键白名单与层名索引（<see cref="PortImplVocabulary"/> 的表达式部分）。
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary>
    /// 表达式对象允许出现的键 = 取值来源键 ∪ 算子键（读取器 fail-fast 与结构门 <c>$defs/expr/properties</c> 的共同取值域）。
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
        nameof(SeamKeys) => SeamKeys,
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

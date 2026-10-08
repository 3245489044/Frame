using BasicMethod.Declarative.Ledger;

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

    private static LedgerKeyClosure? _keyClosure;

    /// <summary>
    /// 分层键闭包（通用框架 <see cref="LedgerKeyClosure"/>）：层名 → 合法键集，
    /// 「未知键 fail-fast」的**唯一实现**。层名 = 各键表的成员名（与调用点 <c>nameof(...)</c> 一致）。
    /// <para>惰性装配：键表分散于多个 partial 文件，静态字段初始化序不定，故首次访问时构造（防悬空引用）。</para>
    /// </summary>
    private static LedgerKeyClosure KeyClosure => _keyClosure ??= new(
    [
        (nameof(ImplKeys), ImplKeys),
        (nameof(ArgKeys), ArgKeys),
        (nameof(ValueSourceKeys), ValueSourceKeys),
        (nameof(DepKeys), DepKeys),
        (nameof(FakeCallKeys), FakeCallKeys),
        (nameof(FakeCaptureKeys), FakeCaptureKeys),
        (nameof(FakeStateKeys), FakeStateKeys),
        (nameof(CallKeys), CallKeys),
        (nameof(ComposedFieldKeys), ComposedFieldKeys),
        (nameof(StepKeys), StepKeys),
        (nameof(ThrowKeys), ThrowKeys),
        (nameof(InvokeKeys), InvokeKeys),
        (nameof(NewKeys), NewKeys),
        (nameof(InitKeys), InitKeys),
        (nameof(ChainKeys), ChainKeys),
        (nameof(DelegateKeys), DelegateKeys),
        (nameof(SaveKeys), SaveKeys),
        (nameof(CollectKeys), CollectKeys),
        (nameof(ScopeKeys), ScopeKeys),
        (nameof(FileKeys), FileKeys),
        (nameof(DeliverKeys), DeliverKeys),
        (nameof(CaptureKeys), CaptureKeys),
        (nameof(ScenarioKeys), ScenarioKeys),
        (nameof(ConcatPartKeys), ConcatPartKeys),
        (nameof(FieldExprKeys), FieldExprKeys),
        (nameof(ExprKeys), ExprKeys),
    ]);

    /// <summary>给定层的键白名单（未登记层 → null = 该层不做未知键校验）；取值域 = 框架键闭包。</summary>
    public static IReadOnlyList<string>? KeysOf(string level)
        => KeyClosure.IsKnownLayer(level) ? KeyClosure.KeysOf(level) : null;

    /// <summary>
    /// 收集层内**未登记键**（保持出现序、去重；未登记层不判）——**唯一实现** = 框架 <see cref="LedgerKeyClosure.EnsureKnown"/>；
    /// 违规文案由调用方注入（域文案留消费方）。
    /// </summary>
    public static void EnsureKnownLayerKeys(
        string layer,
        IEnumerable<string> presentKeys,
        string path,
        Func<string, string, string> messageFactory,
        List<string> violations)
        => KeyClosure.EnsureKnown(layer, presentKeys, path, messageFactory, violations);
}

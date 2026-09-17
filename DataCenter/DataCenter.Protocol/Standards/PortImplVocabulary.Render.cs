namespace DataCenter.Protocol.Standards;

/// <summary>
/// 词表**渲染面**（<see cref="PortImplVocabulary"/> 的渲染部分）：结构门（schema <c>enum</c>）
/// 与内核异常文案共用的读数口。
/// <para>
/// 全部成员**委托既有词表常量**，不新增第二份数据；消费方据此派生，禁自行抄写词表。
/// </para>
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary><c>impl.kind</c> 渲染值。</summary>
    public static IReadOnlyList<string> KindEnum => Kinds;

    /// <summary><c>convert</c> 渲染值。</summary>
    public static IReadOnlyList<string> ConversionEnum => Conversions;

    /// <summary><c>from</c> 渲染值。</summary>
    public static IReadOnlyList<string> FromEnum => ValueSourceFromAll;

    /// <summary>组合投影 <c>select</c> 渲染值。</summary>
    public static IReadOnlyList<string> SelectEnum => Selects;

    /// <summary>替身 <c>fake</c> 渲染值。</summary>
    public static IReadOnlyList<string> FakeKindEnum => FakeKinds;

    /// <summary>作用域 <c>scope.kind</c> 渲染值。</summary>
    public static IReadOnlyList<string> ScopeKindEnum => ScopeKinds;

    /// <summary>载体 <c>deliver.kind</c> 渲染值。</summary>
    public static IReadOnlyList<string> DeliverKindEnum => DeliverKinds;

    /// <summary>依赖形态渲染值。</summary>
    public static IReadOnlyList<string> DependencyFormEnum => DepForms;

    /// <summary>表处置 <c>onBlankFirstKey</c> 渲染值。</summary>
    public static IReadOnlyList<string> OnBlankFirstKeyEnum => OnBlankFirstKey;

    /// <summary>表处置 <c>onDuplicate</c> 渲染值。</summary>
    public static IReadOnlyList<string> OnDuplicateEnum => OnDuplicate;

    /// <summary>聚合算子 <c>aggOp</c> 渲染值。</summary>
    public static IReadOnlyList<string> AggOpEnum => AggOps;

    /// <summary>issue 消息口径 <c>message</c> 渲染值。</summary>
    public static IReadOnlyList<string> MessageModeEnum => MessageModes;
}

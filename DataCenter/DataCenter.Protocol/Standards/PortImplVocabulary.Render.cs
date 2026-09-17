namespace DataCenter.Protocol.Standards;

/// <summary>
/// 词表**渲染面**（<see cref="PortImplVocabulary"/> 的渲染部分）：结构门（schema <c>enum</c>）、
/// 内核异常文案词表段与内核择路判定源共用的读数口。
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

    /// <summary>
    /// <c>convert</c> 脚本通道渲染值（共同 ∪ 脚本特有）：<c>dep</c> 是组合通道指记，脚本分支无此处置，
    /// 故渲染面按**通道**分列（防内核文案列出无分支的处置）。
    /// </summary>
    public static IReadOnlyList<string> ScriptedConversionEnum => [.. CommonConversions, .. ExtendedConversions];

    /// <summary>
    /// <c>convert</c> 共同词渲染值：**反射通道与组合通道共用**（两通道都只认共同词；
    /// 脚本特有词由 <see cref="ScriptedConversionEnum"/> 单列）。
    /// </summary>
    public static IReadOnlyList<string> CommonConversionEnum => CommonConversions;

    /// <summary>
    /// 词表 → 清单文案（<c>|</c> 分隔的**唯一渲染口径**）：内核异常文案的词表段一律经此派生，
    /// 禁在调用点手抄清单（防漏词 / 顺序漂移）。
    /// </summary>
    public static string RenderList(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join("|", values);
    }

    /// <summary>
    /// 绑定方式名（<c>impl.kind</c> 的具名读音）：内核**择路判定源**（<c>PortSpecCompiler</c> /
    /// <c>PortBinding</c> / <c>PortComposer</c>）据此比较，禁手写字面量。
    /// </summary>
    public static class KindName
    {
        /// <summary>委派族（<c>impl</c> 缺省时的实现归属）。</summary>
        public const string Delegate = "delegate";

        /// <summary>反射族。</summary>
        public const string Reflective = "reflective";

        /// <summary>组合族。</summary>
        public const string Composed = "composed";

        /// <summary>脚本族。</summary>
        public const string Scripted = "scripted";

        /// <summary>冻结族（只登记不绑定）。</summary>
        public const string Frozen = "frozen";
    }
}

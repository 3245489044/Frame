using BasicMethod.Declarative.Ledger;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口 <c>impl</c> 词表与逐层键白名单（标准框架通用化重构 §4.1/§4.2 的机器可判定形态）：
/// 此前这些词表散在内核（<c>PortValueOptions</c>、<c>ScriptedArgSpec.From</c>、<c>ComposedFieldSpec.Select</c>、
/// <c>ScriptedDeliverSpec.Kind</c>、<c>ScriptedScopeSpec.Kind</c> …），标准层零处 —— 本类把它们上收为**唯一 C# 源**。
/// <para>
/// 纪律（SingleImplementation）：① 内核执行侧只保留「字符串 → 执行分支」的解析，**判定与清单以本类为源**
/// （择路判定引用 <see cref="KindName"/>、异常文案词表段引用渲染面，见 <c>PortImplVocabulary.Render.cs</c>）；
/// ② <c>schemas\*.schema.json</c> 内的 <c>enum</c> 是**生成物**——由 <c>SchemaEnumSynchronizer</c> 按本类渲染写回
/// （<c>Verify()</c> 对账 / <c>Sync()</c> 幂等写盘），禁手改；③ 逐层键闭包由 <see cref="KeysOf"/> 派生，
/// 唯一判据 = <c>DataCenter.Standards.StandardJson.EnsureLayerKeys</c>（装载期 fail-fast，schema 侧同源派生）。
/// </para>
/// <para>
/// 机制承载（通用框架）：词表 = <see cref="LedgerTerm"/>（唯一性 / 渲染口径）登记进唯一 <see cref="LedgerVocabulary"/>；
/// 逐层键闭包 = <see cref="LedgerKeyClosure"/>（见 <c>PortImplVocabulary.Keys.Expr.cs</c>）。
/// 本类只提供**领域词内容**，登记 / 闭包判定 / 位序对账由框架承担。
/// </para>
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary>
    /// <c>impl.kind</c>：已知绑定方式（5 = 可声明 4 + 遗留 1）。**可声明**值 = 本表 − 遗留值（SK-28：
    /// <c>delegate</c> 已降为遗留，仅读兼容；新端口/新声明一律 <c>reflective</c>/<c>composed</c>/<c>scripted</c>）。
    /// </summary>
    private static readonly LedgerTerm KindTerm =
        new("kind", ["delegate", "reflective", "composed", "scripted", "frozen"]);

    /// <summary><c>convert</c>：三通道**共同**转换词（7）。</summary>
    private static readonly LedgerTerm CommonConversionTerm =
        new("convert.common", ["string", "bool", "int", "double", "csv", "json", "object"]);

    /// <summary><c>convert</c>：脚本通道特有（3）。</summary>
    private static readonly LedgerTerm ExtendedConversionTerm =
        new("convert.extended", ["array", "graph", "passthrough"]);

    /// <summary>
    /// <c>convert</c>：组合通道特有指记（依赖直投）—— <c>dep</c> 与 <c>dep</c> 下标连用，
    /// 表示「本实参 = 该依赖实例」（对照内核 <c>ComposedArgSpec.Convert</c> 注释与 <c>ComposedArgs.Convert</c>）。
    /// </summary>
    public const string DependencyConversion = "dep";

    /// <summary><c>convert</c> 全词表（共同 ∪ 脚本特有 ∪ 组合指记）；空串 = 无转换声明（合法）。</summary>
    private static readonly LedgerTerm ConversionTerm =
        new("convert", [.. CommonConversionTerm.Words, .. ExtendedConversionTerm.Words, DependencyConversion]);

    /// <summary><c>impl.kind</c> 词序列（唯一 C# 源）。</summary>
    public static IReadOnlyList<string> Kinds => KindTerm.Words;

    /// <summary><c>convert</c>：三通道**共同**转换词（7）。</summary>
    public static IReadOnlyList<string> CommonConversions => CommonConversionTerm.Words;

    /// <summary><c>convert</c>：脚本通道特有（3）。</summary>
    public static IReadOnlyList<string> ExtendedConversions => ExtendedConversionTerm.Words;

    /// <summary><c>convert</c> 全词表（共同 ∪ 脚本特有 ∪ 组合指记）；空串 = 无转换声明（合法）。</summary>
    public static IReadOnlyList<string> Conversions => ConversionTerm.Words;

    /// <summary>取值来源 <c>from</c>：六选一基本形态。</summary>
    private static readonly LedgerTerm ValueSourceFromTerm =
        new("from", ["arg", "with", "item", "var", "dep", "literal"]);

    /// <summary>
    /// 取值来源 <c>from</c>：值源扩展形态（脚本通道）。
    /// <para>
    /// 取值域以执行器 <c>ScriptedValue</c> 的 <c>From</c> 分派为准（实测枚举）：<c>row</c>（遍历行单元格）/
    /// <c>member</c>（静态成员）/ <c>type</c>（类型字面量）/ <c>tmpDir</c>/<c>tmpPath</c>（作用域路径）等。
    /// </para>
    /// <para>
    /// 辨异（防两处同名混淆）：<see cref="CollectStandard.From"/> 是**收集源槽名**（不透明名，实测出现
    /// <c>from:"exec"</c>/<c>from:"executor"</c>），**不属本词表**、不做枚举校验。
    /// </para>
    /// <para>
    /// 辨异（G4）：<c>file</c> = 读**仓库相对真实文件**（只读 + fail-fast），与 <c>tmpPath</c>/<c>tmpDir</c>
    /// （临时盘作用域内文件）分属不同资源域；前者用 <c>path</c>（仓库相对路径），后者用 <c>scope</c> + <c>name</c>。
    /// </para>
    /// </summary>
    private static readonly LedgerTerm ValueSourceExtendedFromTerm =
        new("from.extended", ["tmpPath", "tmpDir", "table", "presence", "list", "map", "delegate", "new", "row", "member", "type", "file"]);

    /// <summary><c>from</c> 全词表（基本 ∪ 扩展）。</summary>
    private static readonly LedgerTerm ValueSourceFromAllTerm =
        new("from.all", [.. ValueSourceFromTerm.Words, .. ValueSourceExtendedFromTerm.Words]);

    /// <summary>组合投影 <c>select</c>：取值法（16）。</summary>
    private static readonly LedgerTerm SelectTerm =
        new("select",
        [
            "value", "count", "notNull", "text", "countEqual", "path", "pathText", "message", "thrown", "fakeCount",
            "typeName", "capturedCount", "capturedFirst", "capturedLast", "capturedFirstOrNull", "capturedLastOrNull",
        ]);

    /// <summary>取值来源 <c>from</c>：六选一基本形态。</summary>
    public static IReadOnlyList<string> ValueSourceFrom => ValueSourceFromTerm.Words;

    /// <summary>取值来源 <c>from</c>：值源扩展形态（脚本通道）。</summary>
    public static IReadOnlyList<string> ValueSourceExtendedFrom => ValueSourceExtendedFromTerm.Words;

    /// <summary><c>from</c> 全词表。</summary>
    public static IReadOnlyList<string> ValueSourceFromAll => ValueSourceFromAllTerm.Words;

    /// <summary>组合投影 <c>select</c>：取值法（16）。</summary>
    public static IReadOnlyList<string> Selects => SelectTerm.Words;

    /// <summary>载体 <c>deliver.kind</c>（3）。</summary>
    private static readonly LedgerTerm DeliverKindTerm = new("deliver.kind", ["value", "issues", "action"]);

    /// <summary>作用域 <c>scope.kind</c>（3）。</summary>
    private static readonly LedgerTerm ScopeKindTerm = new("scope.kind", ["tempDir", "using", "restore"]);

    /// <summary>依赖形态：<c>new</c> / <c>factory</c> / <c>fake</c> 恰一（三选一）。</summary>
    private static readonly LedgerTerm DepFormTerm = new("dep.form", ["new", "factory", "fake"]);

    /// <summary>
    /// 替身 <c>fake</c> 种类（6；= 执行器支持集，禁列未实现词——结构门放行而执行器无分支即假阳性放行）。
    /// <c>stateful</c> 由 <c>deps[].state</c>（<c>write</c>/<c>read</c> 成员 + <c>key</c> 实参下标）声明内存键值「写后读回」。
    /// </summary>
    private static readonly LedgerTerm FakeKindTerm =
        new("fake", ["recording", "counting", "fixed", "sequence", "notExpected", "stateful"]);

    /// <summary>表处置 <c>onBlankFirstKey</c>：<c>fail</c>（缺省）/ <c>skip</c>。</summary>
    private static readonly LedgerTerm OnBlankFirstKeyTerm = new("onBlankFirstKey", ["fail", "skip"]);

    /// <summary>表处置 <c>onDuplicate</c>：<c>fail</c>（缺省）/ <c>last</c>。</summary>
    private static readonly LedgerTerm OnDuplicateTerm = new("onDuplicate", ["fail", "last"]);

    /// <summary>聚合算子 <c>aggOp</c>：<c>sum</c>（缺省）/ <c>max</c> / <c>min</c> / <c>avg</c>。</summary>
    private static readonly LedgerTerm AggOpTerm = new("aggOp", ["sum", "max", "min", "avg"]);

    /// <summary>issue 消息口径 <c>message</c>：<c>Message</c>（缺省）/ <c>TypeName</c>。</summary>
    private static readonly LedgerTerm MessageModeTerm = new("message", ["Message", "TypeName"]);

    /// <summary>
    /// 词表册（通用框架 <see cref="LedgerVocabulary"/>）：全部命名枚举词表的**唯一登记点**
    /// （重名 / 同表重词即抛）。声明于全部词表之后（静态初始化按声明序，防悬空引用）。
    /// </summary>
    private static readonly LedgerVocabulary Terms = new(
    [
        KindTerm, CommonConversionTerm, ExtendedConversionTerm, ConversionTerm,
        ValueSourceFromTerm, ValueSourceExtendedFromTerm, ValueSourceFromAllTerm, SelectTerm,
        DeliverKindTerm, ScopeKindTerm, DepFormTerm, FakeKindTerm,
        OnBlankFirstKeyTerm, OnDuplicateTerm, AggOpTerm, MessageModeTerm,
    ]);

    /// <summary>词表册读数口（按名取用；未登记即抛——防拼写漂移静默）。</summary>
    public static LedgerVocabulary Vocabulary => Terms;

    /// <summary>载体 <c>deliver.kind</c>（3）。</summary>
    public static IReadOnlyList<string> DeliverKinds => DeliverKindTerm.Words;

    /// <summary>作用域 <c>scope.kind</c>（3）。</summary>
    public static IReadOnlyList<string> ScopeKinds => ScopeKindTerm.Words;

    /// <summary>依赖形态：<c>new</c> / <c>factory</c> / <c>fake</c> 恰一（三选一）。</summary>
    public static IReadOnlyList<string> DepForms => DepFormTerm.Words;

    /// <summary>替身 <c>fake</c> 种类（6）。</summary>
    public static IReadOnlyList<string> FakeKinds => FakeKindTerm.Words;

    /// <summary>表处置 <c>onBlankFirstKey</c>：<c>fail</c>（缺省）/ <c>skip</c>。</summary>
    public static IReadOnlyList<string> OnBlankFirstKey => OnBlankFirstKeyTerm.Words;

    /// <summary>表处置 <c>onDuplicate</c>：<c>fail</c>（缺省）/ <c>last</c>。</summary>
    public static IReadOnlyList<string> OnDuplicate => OnDuplicateTerm.Words;

    /// <summary>聚合算子 <c>aggOp</c>：<c>sum</c>（缺省）/ <c>max</c> / <c>min</c> / <c>avg</c>。</summary>
    public static IReadOnlyList<string> AggOps => AggOpTerm.Words;

    /// <summary>issue 消息口径 <c>message</c>：<c>Message</c>（缺省）/ <c>TypeName</c>。</summary>
    public static IReadOnlyList<string> MessageModes => MessageModeTerm.Words;

    /// <summary><c>convert</c> 是否已登记（空 = 无转换声明，合法）。登记判定 = 框架 <see cref="LedgerTerm.Contains"/>。</summary>
    public static bool IsKnownConversion(string? convert)
        => string.IsNullOrEmpty(convert) || ConversionTerm.Contains(convert);

    /// <summary><c>kind</c> 是否已登记（含遗留值：读兼容）。</summary>
    public static bool IsKnownKind(string? kind) => kind is not null && KindTerm.Contains(kind);

    /// <summary>
    /// <c>kind</c> 是否为**可声明**值（SK-28：新端口/新声明只许 4 种；<c>delegate</c> 属遗留，仅读兼容）。
    /// 派生自 <see cref="IsKnownKind"/> − <see cref="IsLegacyKind"/>（禁第二份词表数据）。
    /// </summary>
    public static bool IsDeclarableKind(string? kind) => IsKnownKind(kind) && !IsLegacyKind(kind);

    /// <summary>
    /// <c>kind</c> 是否为**遗留**值（只读兼容；**只减不增**，存量条数由守卫棘轮冻结）——当前唯一 = <c>delegate</c>
    /// （按 <c>id</c> 查实现字典；需运行期实例，声明面无法表达）。新增禁止（SK-28）。
    /// </summary>
    public static bool IsLegacyKind(string? kind)
        => string.Equals(kind, KindName.Delegate, StringComparison.Ordinal);

    /// <summary><c>from</c> 是否已登记（空 = 无来源声明，合法）。</summary>
    public static bool IsKnownFrom(string? from)
        => string.IsNullOrEmpty(from) || ValueSourceFromAllTerm.Contains(from);

    /// <summary>聚合算子 <c>aggOp</c> / <c>agg.op</c> 是否已登记（空 = 无声明，按缺省 <c>sum</c>，合法）。</summary>
    public static bool IsKnownAggOp(string? op)
        => string.IsNullOrEmpty(op) || AggOpTerm.Contains(op);

    /// <summary>issue 消息口径 <c>capture.message</c> 是否已登记（空 = 缺省 <c>Message</c>，合法）。</summary>
    public static bool IsKnownMessageMode(string? mode)
        => string.IsNullOrEmpty(mode) || MessageModeTerm.Contains(mode);
}

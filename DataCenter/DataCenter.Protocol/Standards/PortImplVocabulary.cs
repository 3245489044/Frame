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
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary><c>impl.kind</c>：绑定方式（5）。</summary>
    public static readonly IReadOnlyList<string> Kinds =
        ["delegate", "reflective", "composed", "scripted", "frozen"];

    /// <summary><c>kind</c> 缺省值（<c>impl</c> 缺失或为简写字符串时）。</summary>
    public const string DefaultKind = "delegate";

    /// <summary><c>convert</c>：三通道**共同**转换词（7）。</summary>
    public static readonly IReadOnlyList<string> CommonConversions =
        ["string", "bool", "int", "double", "csv", "json", "object"];

    /// <summary><c>convert</c>：脚本通道特有（3）。</summary>
    public static readonly IReadOnlyList<string> ExtendedConversions = ["array", "graph", "passthrough"];

    /// <summary>
    /// <c>convert</c>：组合通道特有指记（依赖直投）—— <c>dep</c> 与 <c>dep</c> 下标连用，
    /// 表示「本实参 = 该依赖实例」（对照内核 <c>ComposedArgSpec.Convert</c> 注释与 <c>ComposedArgs.Convert</c>）。
    /// </summary>
    public const string DependencyConversion = "dep";

    /// <summary><c>convert</c> 全词表（共同 ∪ 脚本特有 ∪ 组合指记）；空串 = 无转换声明（合法）。</summary>
    public static IReadOnlyList<string> Conversions => [.. CommonConversions, .. ExtendedConversions, DependencyConversion];

    /// <summary>取值来源 <c>from</c>：六选一基本形态。</summary>
    public static readonly IReadOnlyList<string> ValueSourceFrom =
        ["arg", "with", "item", "var", "dep", "literal"];

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
    /// </summary>
    public static readonly IReadOnlyList<string> ValueSourceExtendedFrom =
        ["tmpPath", "tmpDir", "table", "presence", "delegate", "new", "row", "member", "type"];

    /// <summary><c>from</c> 全词表。</summary>
    public static IReadOnlyList<string> ValueSourceFromAll => [.. ValueSourceFrom, .. ValueSourceExtendedFrom];

    /// <summary>组合投影 <c>select</c>：取值法（10）。</summary>
    public static readonly IReadOnlyList<string> Selects =
        ["value", "count", "notNull", "text", "countEqual", "path", "pathText", "message", "thrown", "fakeCount"];

    /// <summary>载体 <c>deliver.kind</c>（3）。</summary>
    public static readonly IReadOnlyList<string> DeliverKinds = ["value", "issues", "action"];

    /// <summary>作用域 <c>scope.kind</c>（3）。</summary>
    public static readonly IReadOnlyList<string> ScopeKinds = ["tempDir", "using", "restore"];

    /// <summary>依赖形态：<c>new</c> / <c>factory</c> / <c>fake</c> 恰一（三选一）。</summary>
    public static readonly IReadOnlyList<string> DepForms = ["new", "factory", "fake"];

    /// <summary>替身 <c>fake</c> 种类（5；= 执行器支持集，禁列未实现词——结构门放行而执行器无分支即假阳性放行）。</summary>
    public static readonly IReadOnlyList<string> FakeKinds =
        ["recording", "counting", "fixed", "sequence", "notExpected"];

    /// <summary>表处置 <c>onBlankFirstKey</c>：<c>fail</c>（缺省）/ <c>skip</c>。</summary>
    public static readonly IReadOnlyList<string> OnBlankFirstKey = ["fail", "skip"];

    /// <summary>表处置 <c>onDuplicate</c>：<c>fail</c>（缺省）/ <c>last</c>。</summary>
    public static readonly IReadOnlyList<string> OnDuplicate = ["fail", "last"];

    /// <summary>聚合算子 <c>aggOp</c>：<c>sum</c>（缺省）/ <c>max</c> / <c>min</c> / <c>avg</c>。</summary>
    public static readonly IReadOnlyList<string> AggOps = ["sum", "max", "min", "avg"];

    /// <summary>issue 消息口径 <c>message</c>：<c>Message</c>（缺省）/ <c>TypeName</c>。</summary>
    public static readonly IReadOnlyList<string> MessageModes = ["Message", "TypeName"];

    /// <summary><c>convert</c> 是否已登记（空 = 无转换声明，合法）。</summary>
    public static bool IsKnownConversion(string? convert)
        => string.IsNullOrEmpty(convert)
           || CommonConversions.Contains(convert, StringComparer.Ordinal)
           || ExtendedConversions.Contains(convert, StringComparer.Ordinal)
           || string.Equals(convert, DependencyConversion, StringComparison.Ordinal);

    /// <summary><c>kind</c> 是否已登记。</summary>
    public static bool IsKnownKind(string? kind) => kind is not null && Kinds.Contains(kind, StringComparer.Ordinal);

    /// <summary><c>from</c> 是否已登记（空 = 无来源声明，合法）。</summary>
    public static bool IsKnownFrom(string? from)
        => string.IsNullOrEmpty(from) || ValueSourceFromAll.Contains(from, StringComparer.Ordinal);

    /// <summary>聚合算子 <c>aggOp</c> / <c>agg.op</c> 是否已登记（空 = 无声明，按缺省 <c>sum</c>，合法）。</summary>
    public static bool IsKnownAggOp(string? op)
        => string.IsNullOrEmpty(op) || AggOps.Contains(op, StringComparer.Ordinal);

    /// <summary>issue 消息口径 <c>capture.message</c> 是否已登记（空 = 缺省 <c>Message</c>，合法）。</summary>
    public static bool IsKnownMessageMode(string? mode)
        => string.IsNullOrEmpty(mode) || MessageModes.Contains(mode, StringComparer.Ordinal);
}

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口 <c>impl</c> 的**逐层键白名单**（标准框架通用化重构 §2.3「未知键 fail-fast」的机器可判定形态）：
/// 每一张表 = 某一层声明对象的合法键集合。**消费点**（唯一）：
/// ① 框架键闭包 <c>KeyClosure</c>（<c>PortImplVocabulary.Keys.Expr.cs</c>；已登记层 → 装载期读取器
/// <c>StandardJson.EnsureLayerKeys</c> 据此 fail-fast）；② 守卫 / 完备性对拍（表 ↔ 结构门 / 模型，零运行）；
/// ③ schema 据此写 <c>additionalProperties:false</c>（生成物，由 <c>SchemaEnumSynchronizer</c> 写回）。
/// <para>
/// 注意 <c>ComposedArgKeys</c> 当前**未登记**进键闭包（读取器不校验该层，键集仅由对拍守护）；
/// 其余各表均已登记。开放映射（**不做闭合**）只有两处，因为其键是**不透明名**而非结构键：
/// ① <c>impl.bind</c>（槽名，如 <c>tool</c>/<c>kindArg</c>）；② <c>scenarios.cases</c>（场景名，如 <c>safeOrEmpty</c>）。
/// 同理，载荷叶节点（<c>value</c>/<c>literal</c>/<c>default</c>/<c>rows</c>/<c>values</c>）内部结构由用例自定义，
/// 标准层不解释、不闭合。
/// </para>
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary><c>ports[x].impl</c> 顶层键。</summary>
    public static readonly IReadOnlyList<string> ImplKeys =
    [
        "kind", "id", "target", "method", "reason", "legacyKind", "productFields",
        "args", "bind", "deps", "calls", "project", "script", "deliver", "scopes", "scenarios",
    ];

    /// <summary>实参声明键（<see cref="PortArgStandard"/>；<c>impl.args</c> 反射族）。</summary>
    public static readonly IReadOnlyList<string> ArgKeys =
        ["convert", "optional", "nullLiteral", "default", "from", "key"];

    /// <summary>
    /// 组合族实参声明键（<c>deps[].args</c> / <c>calls[].args</c>）：与反射族**不同的一套**——
    /// 组合族以 <c>argIndex</c> 表达位置实参、<c>dep</c>/<c>value</c>/<c>elementType</c> 表达依赖与常量注入。
    /// <para>辨异：脚本族实参用 <see cref="ValueSourceKeys"/>（<c>from</c> 驱动的取值来源）。</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ComposedArgKeys =
        ["convert", "argIndex", "key", "dep", "value", "elementType", "optional", "default", "nullLiteral"];

    /// <summary>取值来源键（<see cref="ValueSourceStandard"/>；含 <c>table</c>/<c>presence</c> 内层键）。</summary>
    public static readonly IReadOnlyList<string> ValueSourceKeys =
    [
        "from", "index", "key", "dep", "value", "table", "presence", "list", "map", "arg", "with", "var",
        "slot", "item", "literal", "path", "convert", "optional", "default", "nullLiteral", "type",
        "genericArgs", "scope", "name", "rows", "items", "pairs", "rowValue", "where", "separator",
        "onBlankFirstKey", "onDuplicate", "distinct", "elementType", "construct", "new", "named",
        "delegate", "json", "content",
    ];

    /// <summary>依赖声明键（<see cref="DepStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> DepKeys =
        ["new", "factory", "fake", "contract", "wrap", "methods", "values", "genericArgs", "args", "calls", "capture", "state"];

    /// <summary>
    /// 有状态替身键（<see cref="FakeStateStandard"/>；<c>deps[].state</c>）：
    /// <c>write</c>（写成员）/ <c>read</c>（读成员）/ <c>key</c>（键实参下标，缺省 0）。
    /// </summary>
    public static readonly IReadOnlyList<string> FakeStateKeys = ["write", "read", "key"];

    /// <summary>替身调用捕获键（<see cref="FakeCaptureStandard"/>；<c>deps[].capture</c>；G8）。</summary>
    public static readonly IReadOnlyList<string> FakeCaptureKeys = ["member"];

    /// <summary>替身应答脚本项键（<see cref="FakeCallStandard"/>；<c>deps[].calls[]</c>；G1）。</summary>
    public static readonly IReadOnlyList<string> FakeCallKeys =
        ["member", "position", "value", "throw", "message"];

    /// <summary>组合调用声明键（<see cref="CallStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> CallKeys =
        ["as", "target", "method", "property", "dep", "await", "expect", "args"];

    /// <summary>组合投影声明键（<see cref="ComposedFieldStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ComposedFieldKeys =
        ["field", "call", "select", "other", "path", "dep", "method", "key"];
}

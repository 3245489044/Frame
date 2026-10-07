using System.Text.Json;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// 组合族实参声明（标准框架通用化重构 §2.2；对应内核 <c>ComposedArgSpec</c>）：
/// 与反射族（<see cref="PortArgStandard"/>）**不同的一套**——位置实参用 <see cref="ArgIndex"/>，
/// 依赖注入用 <see cref="Dep"/>（配 <c>convert:"dep"</c>），常量注入用 <see cref="Value"/>，
/// 数组元素类型用 <see cref="ElementType"/>。
/// </summary>
/// <param name="Convert">转换规则（取值域见 <see cref="PortImplVocabulary.Conversions"/>）。</param>
/// <param name="ArgIndex">用例位置实参索引（<c>.specs</c> 的 <c>args</c>；越界 → <see cref="Optional"/> 决定默认或抛）。</param>
/// <param name="Key">具名实参键（<c>with</c>；<c>json</c>/<c>array</c> 用）。</param>
/// <param name="Dep">依赖下标（<c>dep</c>/<c>array</c> 元素类型注入用）。</param>
/// <param name="Value">常量 JSON（<c>json</c> 直传 / <c>array</c> 的元素集；载荷位）。</param>
/// <param name="ElementType">数组元素类型全名（<c>array</c> 用）。</param>
/// <param name="Optional">可省略（无值 → <see cref="Default"/> 或目标类型默认值）。</param>
/// <param name="Default">缺省值（载荷位）。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
public sealed record ComposedArgStandard(
    string Convert,
    int? ArgIndex = null,
    string? Key = null,
    int? Dep = null,
    JsonElement? Value = null,
    string? ElementType = null,
    bool Optional = false,
    JsonElement? Default = null,
    bool NullLiteral = false);

/// <summary>
/// 替身应答脚本项（<c>deps[].calls[]</c>；G1）：按「成员名 / 调用序」给应答或抛指定异常。
/// <para>只承载「值 / 结构 / 声明」——无控制流、无领域语义词、不引入表达式语言。</para>
/// </summary>
/// <param name="Member">目标成员名（方法名）；空 = 任意成员。</param>
/// <param name="Position">调用序（该成员第 N 次调用，0 起）；空 = 该成员每次调用都适用。</param>
/// <param name="Value">应答值（<see cref="HasValue"/> 区分「应答 null」与「未给值」）。</param>
/// <param name="HasValue">应答已给值（<c>value: null</c> 亦为给值）。</param>
/// <param name="Throw">抛出异常类型全名（与 <see cref="Value"/> 二选一）。</param>
/// <param name="Message">抛出异常消息（可空）。</param>
public sealed record FakeCallStandard(
    string? Member = null,
    int? Position = null,
    JsonElement? Value = null,
    bool HasValue = false,
    string? Throw = null,
    string? Message = null);

/// <summary>
/// 替身**调用捕获**声明（<c>deps[].capture</c>；G8）：被测方回调时把实参数组按调用序记录下来，
/// 供投影取值法 <c>capturedCount</c> / <c>capturedFirst</c> / <c>capturedLast</c> 读取
/// （补 <c>fakeCount</c> 只有计数、无实参/末次值之缺）。
/// </summary>
/// <param name="Member">被捕获的成员名（方法名）；空 = 全部成员。</param>
public sealed record FakeCaptureStandard(string? Member = null);

/// <summary>
/// 组合族依赖声明（标准框架通用化重构 §2.2 第 4 项；对应内核 <c>ComposedDepSpec</c>）：
/// <c>new</c>（构造）｜<c>factory</c>（数据半工厂）｜<c>fake</c>（代理替身，可包装既有依赖）三选一。
/// </summary>
/// <param name="New">构造目标类型全名（泛型开放类型形如 <c>Ns.Type`1</c>）。</param>
/// <param name="Factory">数据半工厂名（不透明字符串；由装配方注入的工厂表解析）。</param>
/// <param name="Fake">代理替身种类（取值域见 <see cref="PortImplVocabulary.FakeKinds"/>）。</param>
/// <param name="Contract">替身实现的契约接口全名（<see cref="Wrap"/> 为空且无 <see cref="Calls"/> 时必填）。</param>
/// <param name="Wrap">包装目标依赖下标（recording/counting 装饰真实实现用）。</param>
/// <param name="Methods">关注的方法名（counting/notExpected 用；空 = 全部方法）。</param>
/// <param name="Values">固定 / 序列应答值（fixed/sequence 用；载荷位）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名。</param>
/// <param name="Args">构造 / 工厂实参声明。</param>
/// <param name="Calls">应答脚本（G1；按成员名 / 调用序给值或抛异常；空 = 不启用）。</param>
/// <param name="Capture">调用捕获（G8；按调用序记录实参；空 = 不捕获）。</param>
public sealed record DepStandard(
    string? New = null,
    string? Factory = null,
    string? Fake = null,
    string? Contract = null,
    int? Wrap = null,
    IReadOnlyList<string>? Methods = null,
    IReadOnlyList<JsonElement>? Values = null,
    IReadOnlyList<string>? GenericArgs = null,
    IReadOnlyList<ComposedArgStandard>? Args = null,
    IReadOnlyList<FakeCallStandard>? Calls = null,
    FakeCaptureStandard? Capture = null);

/// <summary>
/// 组合族单次调用声明（标准框架通用化重构 §2.2 第 5 项；对应内核 <c>ComposedCallSpec</c>）：
/// 二态——<see cref="Dep"/> 为空 = 静态方法 / 静态成员读取；非空 = 对依赖实例调用。
/// </summary>
/// <param name="As">本次调用结果的引用名（供 <see cref="ComposedFieldStandard.Call"/> 引用；同一组合内唯一）。</param>
/// <param name="Target">目标类型全名（静态调用用；实例调用可空）。</param>
/// <param name="Method">目标成员名（方法 / 属性 / 字段）。</param>
/// <param name="IsProperty">true = 成员读取；false = 方法调用。</param>
/// <param name="Dep">调用目标依赖下标（空 = 静态调用）。</param>
/// <param name="Await">true = 结果为 <c>Task</c>/<c>Task&lt;T&gt;</c>，折取为同步值。</param>
/// <param name="Expect">期望异常类型全名：命中即捕获；未命中原样上抛。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
public sealed record CallStandard(
    string As,
    string? Target = null,
    string Method = "",
    bool IsProperty = false,
    int? Dep = null,
    bool Await = false,
    string? Expect = null,
    IReadOnlyList<ComposedArgStandard>? Args = null);

/// <summary>
/// 组合族投影声明（标准框架通用化重构 §2.2 第 6 项；对应内核 <c>ComposedFieldSpec</c>）：
/// 产物字段 ← 某次调用 / 某个替身的取法单源。
/// </summary>
/// <param name="Field">产物字段名（= 契约 <c>out.fields</c> 的元素）。</param>
/// <param name="Call">取值来源调用（<see cref="CallStandard.As"/>；<c>fakeCount</c> 可空）。</param>
/// <param name="Select">取值法（取值域见 <see cref="PortImplVocabulary.Selects"/>）。</param>
/// <param name="Other">仅 <c>countEqual</c> 用：被比较的另一调用引用名。</param>
/// <param name="Path">仅 <c>path</c> / <c>pathText</c> / <c>capturedFirst(OrNull)</c> / <c>capturedLast(OrNull)</c> 用：属性 / 字段路径（后四者为捕获实参上的路径）。</param>
/// <param name="Dep">仅 <c>fakeCount</c> / <c>capturedCount</c> / <c>capturedFirst(OrNull)</c> / <c>capturedLast(OrNull)</c> 用：替身依赖下标。</param>
/// <param name="Method">仅 <c>fakeCount</c> / 捕获族用：被计数 / 被捕获的方法名（捕获族可空 = 全部成员）。</param>
/// <param name="Key">仅 <c>fakeCount</c> 用：主键（空 = 不按键聚合）。</param>
public sealed record ComposedFieldStandard(
    string Field,
    string? Call,
    string Select,
    string? Other = null,
    string? Path = null,
    int? Dep = null,
    string? Method = null,
    string? Key = null)
{
    /// <summary>
    /// 收敛 3（取值代数单轨）：旧 <see cref="Select"/> 捕获 / 计数族 → 代数节点的**单一转换点**。
    /// <para>
    /// 六旧值 → 三节点：<c>capturedFirst/Last(OrNull)</c> → <see cref="CaptureExprStandard"/>、
    /// <c>capturedCount</c> → <see cref="CaptureCountExprStandard"/>、<c>fakeCount</c> → <see cref="FakeCountExprStandard"/>；
    /// 其余取值法 → null（仍由既有取值法分支承担）。转换后共性求值归 <c>ScriptedExprEvaluator</c>，禁再逐档分派。
    /// </para>
    /// </summary>
    public ExprStandard? ToCaptureExpr() => Select switch
    {
        "capturedFirst" => CaptureExpr("first", orNull: false),
        "capturedLast" => CaptureExpr("last", orNull: false),
        "capturedFirstOrNull" => CaptureExpr("first", orNull: true),
        "capturedLastOrNull" => CaptureExpr("last", orNull: true),
        "capturedCount" => new ExprStandard(CaptureCount: new CaptureCountExprStandard(Dep, Method)),
        "fakeCount" => new ExprStandard(FakeCount: new FakeCountExprStandard(Dep, Method, Key)),
        _ => null,
    };

    private ExprStandard CaptureExpr(string at, bool orNull)
        => new(Capture: new CaptureExprStandard(Dep, Method, at, Path: Path, OrNull: orNull));
}


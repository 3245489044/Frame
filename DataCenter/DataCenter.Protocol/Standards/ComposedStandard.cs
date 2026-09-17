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
/// 组合族依赖声明（标准框架通用化重构 §2.2 第 4 项；对应内核 <c>ComposedDepSpec</c>）：
/// <c>new</c>（构造）｜<c>factory</c>（数据半工厂）｜<c>fake</c>（代理替身，可包装既有依赖）三选一。
/// </summary>
/// <param name="New">构造目标类型全名（泛型开放类型形如 <c>Ns.Type`1</c>）。</param>
/// <param name="Factory">数据半工厂名（不透明字符串；由装配方注入的工厂表解析）。</param>
/// <param name="Fake">代理替身种类（取值域见 <see cref="PortImplVocabulary.FakeKinds"/>）。</param>
/// <param name="Contract">替身实现的契约接口全名（<see cref="Wrap"/> 为空时必填）。</param>
/// <param name="Wrap">包装目标依赖下标（recording/counting 装饰真实实现用）。</param>
/// <param name="Methods">关注的方法名（counting/notExpected 用；空 = 全部方法）。</param>
/// <param name="Values">固定 / 序列应答值（fixed/sequence 用；载荷位）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名。</param>
/// <param name="Args">构造 / 工厂实参声明。</param>
public sealed record DepStandard(
    string? New = null,
    string? Factory = null,
    string? Fake = null,
    string? Contract = null,
    int? Wrap = null,
    IReadOnlyList<string>? Methods = null,
    IReadOnlyList<JsonElement>? Values = null,
    IReadOnlyList<string>? GenericArgs = null,
    IReadOnlyList<ComposedArgStandard>? Args = null);

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
/// <param name="Path">仅 <c>path</c> / <c>pathText</c> 用：属性 / 字段路径。</param>
/// <param name="Dep">仅 <c>fakeCount</c> 用：替身依赖下标。</param>
/// <param name="Method">仅 <c>fakeCount</c> 用：被计数的方法名。</param>
/// <param name="Key">仅 <c>fakeCount</c> 用：主键（空 = 不按键聚合）。</param>
public sealed record ComposedFieldStandard(
    string Field,
    string? Call,
    string Select,
    string? Other = null,
    string? Path = null,
    int? Dep = null,
    string? Method = null,
    string? Key = null);

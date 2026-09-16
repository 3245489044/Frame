namespace Strategy.Observability.Ports;

using System.Text.Json;

/// <summary>
/// 组合绑定的**实参**声明（机制件；位序即语义）。取值来源四选一：
/// <list type="bullet">
/// <item><see cref="ArgIndex"/>：用例位置实参（<c>.specs</c> 的 <c>args</c>，按 <see cref="Convert"/> 转换）；</item>
/// <item><see cref="Key"/>：用例具名实参（<c>with</c> 的原始 JSON，按目标形参类型反序列化）；</item>
/// <item><see cref="Dep"/>：同组合内已构建的依赖对象（注入用）；</item>
/// <item><see cref="Value"/>：声明内常量 JSON（如空数组、null、字面结构）。</item>
/// </list>
/// <para>全空 = 未取值：<see cref="Optional"/> → 目标类型默认值，否则 fail-fast。</para>
/// </summary>
/// <param name="Convert">转换规则：<c>string</c> / <c>bool</c> / <c>int</c> / <c>double</c> / <c>csv</c> / <c>json</c> / <c>dep</c>（依赖引用）/ <c>array</c>（空数组或元素数组）/ <c>object</c>（位置实参对象直投：原样传实例，不文本化）。</param>
/// <param name="ArgIndex">用例位置实参索引（<c>.specs</c> 的 <c>args</c>；越界 → <see cref="Optional"/> 决定默认值或抛）。</param>
/// <param name="Key">具名实参键（<c>with</c>；<c>json</c>/<c>array</c> 用）。</param>
/// <param name="Dep">依赖下标（<c>dep</c>/<c>array</c> 元素类型注入用）。</param>
/// <param name="Value">常量 JSON（<c>json</c> 直传 / <c>array</c> 的元素集）。</param>
/// <param name="ElementType">数组元素类型全名（<c>array</c> 用；与 <see cref="Value"/> 的元素反序列化）。</param>
/// <param name="Optional">可省略（无值 → <see cref="Default"/> 或目标类型默认值）。</param>
/// <param name="Default">缺省值。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
public sealed record ComposedArgSpec(
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
/// 组合绑定的**依赖**声明（装配期按声明序构建，供实例调用 / 注入）：
/// <c>new</c>（构造）｜<c>factory</c>（数据半工厂）｜<c>fake</c>（代理替身，可包装既有依赖）。
/// </summary>
/// <param name="New">构造目标类型全名（泛型开放类型形如 <c>Ns.Type`1</c>，闭合实参见 <see cref="GenericArgs"/>）。</param>
/// <param name="Factory">数据半工厂名（不透明字符串；由装配方注入的工厂表解析）。</param>
/// <param name="Fake">代理替身种类：<c>recording</c> / <c>counting</c> / <c>fixed</c> / <c>sequence</c> / <c>notExpected</c>。</param>
/// <param name="Contract">替身实现的契约接口全名（<see cref="Wrap"/> 为空时必填；包装形态自动取宿主类型）。</param>
/// <param name="Wrap">包装目标依赖下标（recording/counting 装饰真实实现用）。</param>
/// <param name="Methods">关注的方法名（counting/notExpected 用；空 = 全部方法）。</param>
/// <param name="Values">固定 / 序列应答值（fixed/sequence 用）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名（<see cref="New"/> 为开放泛型时按序闭合）。</param>
/// <param name="Args">构造 / 工厂实参声明。</param>
public sealed record ComposedDepSpec(
    string? New = null,
    string? Factory = null,
    string? Fake = null,
    string? Contract = null,
    int? Wrap = null,
    IReadOnlyList<string>? Methods = null,
    IReadOnlyList<JsonElement>? Values = null,
    IReadOnlyList<string>? GenericArgs = null,
    IReadOnlyList<ComposedArgSpec>? Args = null);

/// <summary>
/// 组合绑定的**单次调用**声明（机制件；本模型只承载「调谁 / 取什么」，不含任何领域语义——
/// 类型全名与方法名均为不透明字符串）。
/// <para>二态：<see cref="Dep"/> 为空 = 静态方法 / 静态成员读取（<see cref="Target"/>）；非空 = 对依赖实例调用。</para>
/// </summary>
/// <param name="As">本次调用结果的引用名（供 <see cref="ComposedFieldSpec.Call"/> 引用；同一组合内唯一）。</param>
/// <param name="Target">目标类型全名（静态调用用；实例调用可空）。</param>
/// <param name="Method">目标成员名（方法 / 属性 / 字段）。</param>
/// <param name="IsProperty">true = 成员读取（**属性优先，缺失回落字段**：声明只表达「读一个值」，不区分产品表示）；false = 方法调用。</param>
/// <param name="Dep">例调用目标依赖下标（空 = 静态调用）。</param>
/// <param name="Await">true = 结果为 <c>Task</c>/<c>Task&lt;T&gt;</c>，同折取为同步值（异步面折同步的唯一入口）。</param>
/// <param name="Expect">期望异常类型全名：命中即捕获（结果 = 该异常对象，投影按 <c>message</c>/<c>thrown</c> 取值）；未命中原样上抛。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
public sealed record ComposedCallSpec(
    string As,
    string? Target = null,
    string Method = "",
    bool IsProperty = false,
    int? Dep = null,
    bool Await = false,
    string? Expect = null,
    IReadOnlyList<ComposedArgSpec>? Args = null);

/// <summary>
/// 组合绑定的**投影**声明（产物字段 ← 某次调用 / 某个替身的取法单源）：把旧夹具里手写的
/// <c>Product(("a", …), ("b", …))</c> 投影骨架收敛为声明（取值法为机制枚举，不含领域谓词）。
/// </summary>
/// <param name="Field">产物字段名（= 契约 <c>out.fields</c> 的元素）。</param>
/// <param name="Call">取值来源调用（<see cref="ComposedCallSpec.As"/>；<c>fakeCount</c> 可空）。</param>
/// <param name="Select">取值法：<c>value</c>（原样）/ <c>count</c>（计数：集合 / 数组 / 字符串长度）/
/// <c>notNull</c>（非空布尔）/ <c>text</c>（文本形式）/ <c>countEqual</c>（与 <paramref name="Other"/> 计数相等）/
/// <c>path</c>（对象属性路径取值，见 <paramref name="Path"/>）/<c>pathText</c>（属性路径取值后取文本形式，
/// 如枚举成员按名断言）/ <c>message</c>（异常消息）/ <c>thrown</c>（异常是否命中）/
/// <c>fakeCount</c>（读替身的调用计数，见 <paramref name="Dep"/>/<paramref name="Method"/>/<paramref name="Key"/>）。</param>
/// <param name="Other">仅 <c>countEqual</c> 用：被比较的另一调用引用名。</param>
/// <param name="Path">仅 <c>path</c> 用：属性 / 字段路径（<c>A.B.C</c>；逐段属性优先、回落字段；任一段缺失 → null）。</param>
/// <param name="Dep">仅 <c>fakeCount</c> 用：替身依赖下标。</param>
/// <param name="Method">仅 <c>fakeCount</c> 用：被计数的方法名。</param>
/// <param name="Key">仅 <c>fakeCount</c> 用：主键（方法首个字符串实参值；空 = 不按键聚合）。</param>
public sealed record ComposedFieldSpec(
    string Field,
    string? Call,
    string Select,
    string? Other = null,
    string? Path = null,
    int? Dep = null,
    string? Method = null,
    string? Key = null);

/// <summary>
/// 声明式**组合绑定**（E15 续 / 消除通道 C1–C3）：把「装配 → 调用 → 投影」从夹具 C# 变为标准层声明，
/// 使无产品 seam 的编排型端口也能零夹具代码装配。
/// <para>分工红线：本件与 <see cref="ReflectiveBinding"/> 同为机制层——只认不透明字符串 + 反射 + 投影取值法，
/// 不含任何领域类型 / 领域谓词（L1 红线）；领域语义（调哪个 API、取哪些字段）由标准文本承载。</para>
/// </summary>
public sealed class ComposedBindingSpec
{
    public ComposedBindingSpec(
        string portKey,
        IReadOnlyList<ComposedDepSpec>? deps,
        IReadOnlyList<ComposedCallSpec> calls,
        IReadOnlyList<ComposedFieldSpec> project)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey)
            ? throw new ArgumentException("端口键不能为空", nameof(portKey))
            : portKey;
        Deps = deps ?? Array.Empty<ComposedDepSpec>();
        Calls = calls ?? Array.Empty<ComposedCallSpec>();
        Project = project ?? Array.Empty<ComposedFieldSpec>();
    }

    /// <summary>端口键（诊断定位用）。</summary>
    public string PortKey { get; }

    /// <summary>依赖序列（声明序 = 构建序；<c>dep</c> 实参只能引用更早的依赖）。</summary>
    public IReadOnlyList<ComposedDepSpec> Deps { get; }

    /// <summary>调用序列（声明序 = 调用序）。</summary>
    public IReadOnlyList<ComposedCallSpec> Calls { get; }

    /// <summary>投影序列（声明序 = 产物字段序）。</summary>
    public IReadOnlyList<ComposedFieldSpec> Project { get; }

    /// <summary>
    /// 数据半注入的工厂表（<c>dep.factory</c> 用；工厂名 → 求值器）。随声明传递，
    /// 使「声明 → 依赖构建」不需要第二套装配上下文（缺表且声明了 factory 即 fail-fast）。
    /// </summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories { get; init; } =
        new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);
}

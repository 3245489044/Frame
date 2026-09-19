namespace Strategy.Observability.Ports;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 脚本绑定的**取值来源**（bind 槽 / 步骤实参 / 表达式源共用同一口径；位序即语义）。
/// 六选一：<c>arg</c>（用例位置实参）｜<c>with</c>（用例具名实参原始 JSON）｜
/// <c>item</c>（遍历变量：元素 / 元素下标 / 元素属性路径）｜<c>var</c>（脚本槽引用）｜
/// <c>dep</c>（依赖引用）｜<c>literal</c>（声明常量，含 <c>null</c>）。
/// <para>全空 = 无值：<see cref="Optional"/> → 目标类型默认值，否则 fail-fast。</para>
/// </summary>
/// <param name="From">来源种类：<c>arg</c> / <c>with</c> / <c>item</c> / <c>var</c> / <c>dep</c> / <c>literal</c>。</param>
/// <param name="Index">位置实参下标（<c>arg</c>）或元素下标（<c>item</c>）。</param>
/// <param name="Key">具名实参键（<c>with</c>） / 槽名（<c>var</c>） / 遍历变量名（<c>item</c>）。</param>
/// <param name="Dep">依赖下标（<c>dep</c>）。</param>
/// <param name="Path">属性路径（<c>item</c> / <c>var</c> 取元素或槽对象的属性）。</param>
/// <param name="Convert">转换规则：<c>string</c> / <c>bool</c> / <c>int</c> / <c>double</c> / <c>csv</c> / <c>json</c> / <c>object</c>（位置实参对象直投：原样传实例，不文本化）。</param>
/// <param name="Optional">可省略（无值 → <see cref="Default"/> 或目标类型默认值）。</param>
/// <param name="Default">缺省值 JSON。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
/// <param name="Value">声明常量 JSON（<c>literal</c>）。</param>
/// <param name="Type">目标类型全名（缺省 = 原义保留；声明后按该类型反序列化 / 校验）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名（<see cref="Type"/> 为开放泛型时按序闭合）。</param>
/// <param name="Scope">临时目录作用域名（仅 <c>tmpPath</c> / <c>tmpDir</c> 用，对应声明 <c>scopes[].as</c>）。</param>
/// <param name="Name">作用域内文件相对名（仅 <c>tmpPath</c> 用；不得含路径分隔符）。</param>
/// <param name="Rows">行集来源（仅 <c>table</c>：<c>with</c> / <c>var</c> / <c>item</c> 形态的二维行表）。</param>
/// <param name="Items">词元集来源（仅 <c>presence</c>；缺省 = 复用 <see cref="Rows"/>）。</param>
/// <param name="RowValue">行值表达式（<c>table</c> 用；体内 <c>from:row</c> 引当前行单元格；缺省 = 首列文本）。</param>
/// <param name="Filter">行 / 词元过滤表达式（<c>table</c> / <c>presence</c> 用；求值为假即跳过该项）。</param>
/// <param name="Separator">主键列分隔符（<c>table</c>；缺省 <c>|</c>；空串 = 不拼接，键取首列）。</param>
/// <param name="OnBlankFirstKey">首列为空的处置（<c>table</c>：<c>fail</c> 缺省 / <c>skip</c>）。</param>
/// <param name="OnDuplicate">键重复的处置（<c>table</c>：<c>fail</c> 缺省 / <c>last</c>）。</param>
/// <param name="Distinct">词元去重（<c>presence</c> 用）。</param>
/// <param name="ElementType">集合元素类型全名（<c>convert:array</c> 且目标类型未给出元素类型时用）。</param>
/// <param name="Construct">内嵌构造声明（W6：本实参的值 = 构造结果；与 <c>convert:array</c> 连用即数组形参的对象图）。</param>
/// <param name="Named">命名实参（W13：按形参名绑定而非位置；缺省 = 位置绑定）。用于构造函数 / 方法的**可选命名形参**
/// （如 <c>new AgentApiIntentSource(taskPool: null, history: …, nodeStore: …)</c>）。</param>
/// <param name="Delegate">委托工厂声明（W26：<c>from:delegate</c> 时按本声明构造委托；闭包体在调用期执行）。</param>
public sealed record ScriptedArgSpec(
    string? From = null,
    int? Index = null,
    string? Key = null,
    int? Dep = null,
    string? Path = null,
    string? Convert = null,
    bool Optional = false,
    JsonElement? Default = null,
    bool NullLiteral = false,
    JsonElement? Value = null,
    string? Type = null,
    IReadOnlyList<string>? GenericArgs = null,
    string? Scope = null,
    string? Name = null,
    ScriptedArgSpec? Rows = null,
    ScriptedArgSpec? Items = null,
    ScriptedExpr? RowValue = null,
    ScriptedExpr? Filter = null,
    string? Separator = null,
    string? OnBlankFirstKey = null,
    string? OnDuplicate = null,
    bool Distinct = false,
    string? ElementType = null,
    ScriptedNewSpec? Construct = null,
    string? Named = null,
    ScriptedDelegateSpec? Delegate = null)
{
    /// <summary>
    /// 唯一转换表视图（R2-1）：取公共内核字段。脚本通道的扩展字段（<c>table</c>/<c>presence</c>/<c>construct</c>…）
    /// 不属转换表，由 R2-2 的指令模型另行承载。
    /// </summary>
    [JsonIgnore]
    public PortValueOptions Options => new(Convert, Optional, NullLiteral, Default, From, Key);
}

/// <summary>
/// **收集声明**（G1）：把本步骤产物或指定槽的当前值按序追加到收集槽（槽在执行前预建空清单，短路 / 零迭代时投影取之不为空）。
/// <para>多源：<see cref="From"/> 空 = 取本步骤产物；非空 = 取该槽当前值（供 <c>invoke.out</c> 出参逐迭代留存等）。
/// 二者语义不同 —— 步产物是「该步结果」，槽源是「该步之外已存在的值」（见落地记录 X3 的同槽双写禁令）。</para>
/// </summary>
/// <param name="Slot">收集槽名。</param>
/// <param name="From">收集源槽名（空 = 本步骤产物）。</param>
public sealed record ScriptedCollectSpec(string Slot, string? From = null);

/// <summary>
/// 脚本**步骤**（声明序 = 执行序）：<c>new</c>（构造）｜<c>invoke</c>（调用 / 成员读取）｜
/// <c>foreach</c>（遍历）。三者互斥；<see cref="Collect"/> / <see cref="When"/> 为步骤修饰。
/// </summary>
/// <param name="Collects">收集声明（步产物 / 槽值 → 收集槽；G1）。</param>
/// <param name="When">条件短路表达式（G3 + P1 谓词）：求值为真才执行本步骤；<c>⊥</c>（缺槽 / 解析失败）→ 不执行并记录。</param>
/// <param name="New">构造步骤（<c>new</c>）。</param>
/// <param name="Invoke">调用步骤（<c>invoke</c>）。</param>
/// <param name="Foreach">遍历步骤（<c>foreach</c>）。</param>
public sealed record ScriptedStep(
    IReadOnlyList<ScriptedCollectSpec>? Collects = null,
    ScriptedExpr? When = null,
    ScriptedNewSpec? New = null,
    ScriptedInvokeSpec? Invoke = null,
    ScriptedForeachSpec? Foreach = null);

/// <summary>构造步骤：反射构造一个对象并写入槽（等价 <c>deps.new</c>，但可在遍历体内构造）。</summary>
/// <param name="As">结果槽名。</param>
/// <param name="Type">类型全名（开放泛型形如 <c>Ns.Type`1</c>）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名。</param>
/// <param name="Args">构造实参声明。</param>
/// <param name="Chain">链式后置调用（W6：构造后按序调用实例方法；见 <see cref="ScriptedChainSpec"/>）。</param>
/// <param name="Init">构造后属性 / 字段初始化（W13：C# 对象初始化器 <c>new T { P = v, … }</c> 的声明等价；
/// 声明序 = 赋值序；成员缺失即抛）。</param>
public sealed record ScriptedNewSpec(
    string As,
    string Type,
    IReadOnlyList<string>? GenericArgs = null,
    IReadOnlyList<ScriptedArgSpec>? Args = null,
    IReadOnlyList<ScriptedChainSpec>? Chain = null,
    IReadOnlyList<ScriptedInitSpec>? Init = null);

/// <summary>
/// 属性 / 字段初始化项（W13）：<c>{ "member": "RulesFile", "value": { …ArgSpec } }</c>（<c>value</c> 必填）。
/// <para>只做「构造后赋值」这一机制，不含任何领域语义；属性与字段不区分（与 <c>member</c> 读取同口径），
/// 对象初始化器 / <c>init</c> 访问器成员同样适用。</para>
/// </summary>
/// <param name="Member">成员名（属性优先，回落字段）。</param>
/// <param name="Value">取值声明（<c>arg</c> / <c>with</c> / <c>var</c> / <c>literal</c> / <c>member</c> / <c>new</c> …）。</param>
public sealed record ScriptedInitSpec(string Member, ScriptedArgSpec? Value = null);

/// <summary>
/// 链式后置调用（W6：对象图构造）：对已构造实例按序调用方法（<c>Add</c> 一类）。
/// <para>返回值默认**忽略**（实例即链值，适配 <c>void</c> / <c>bool</c> 返回的 <c>Add</c>）；
/// 声明 <see cref="Rebind"/> = true 时以返回值替换链值（适配返回新实例的流式 API）。</para>
/// </summary>
/// <param name="Method">实例方法名。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
/// <param name="Rebind">true = 以本调用的返回值替换链值。</param>
public sealed record ScriptedChainSpec(
    string Method,
    IReadOnlyList<ScriptedArgSpec>? Args = null,
    bool Rebind = false);

/// <summary>
/// **委托工厂声明**（W26：<c>from:delegate</c> 的值来源）：按 <see cref="Kind"/> 闭合委托类型后构造一个委托，
/// 其**调用体**在委托被调用时执行 <see cref="Steps"/> 并取 <see cref="Result"/> 为返回值。
/// <para>闭包语义：委托体复用**创建时**的作用域（外层槽 / 收集槽可见）⇒ 体内 <c>collect</c> 即为「带副作用的闭包」
/// （调用期留痕）；委托实参以帧名 <c>argN</c>（或 <see cref="Parameters"/> 的形参名）注入，体内 <c>from:item</c> 取之。
/// 体为异步任务时按「非 <c>Task</c> 结果 → <c>Task.FromResult</c> 包装」对齐声明返回类型。</para>
/// <para>零领域词汇：委托类型 / 帧名均为不透明字符串；本件只提供「声明式可构造的委托」这一机制，
/// 具体业务语义仍由类型 / 方法名承载。</para>
/// </summary>
/// <param name="Kind">委托类型全名（如 <c>System.Func&lt;object,System.Threading.Tasks.Task&lt;object&gt;&gt;</c>）。</param>
/// <param name="Steps">调用期执行的步骤序列（声明序 = 执行序）。</param>
/// <param name="Result">返回值表达式（缺省 = 末步骤产物）。</param>
/// <param name="Parameters">形参帧名序列（缺省 = <c>arg0</c>/<c>arg1</c>/…；给名字时体内可用该名引用实参）。</param>
/// <param name="Collect">调用留痕收集槽（非空时每次调用把实参数组追加到该槽——副作用可观测）。</param>
/// <param name="Await">true = 体产物为 <c>Task</c>/<c>Task&lt;T&gt;</c> 时折取（声明返回类型非任务时用）。</param>
public sealed record ScriptedDelegateSpec(
    string? Kind = null,
    IReadOnlyList<ScriptedStep>? Steps = null,
    ScriptedExpr? Result = null,
    IReadOnlyList<string>? Parameters = null,
    string? Collect = null,
    bool Await = false);

/// <summary>
/// 进入期**取值留存**项（W27：<c>restore</c> 作用域用）：把静态成员当前值读入槽，供退出期写回。
/// </summary>
/// <param name="Slot">留存槽名。</param>
/// <param name="Type">成员所属类型全名。</param>
/// <param name="Key">成员名（属性优先、回落字段）。</param>
/// <param name="GenericArgs">类型泛型闭合实参（开放泛型类型用）。</param>
public sealed record ScriptedSaveSpec(
    string Slot,
    string Type,
    string Key,
    IReadOnlyList<string>? GenericArgs = null);

/// <summary>
/// 调用步骤：方法调用 / 成员读取（静态或对依赖实例）。可异步折取、可期望异常、可捕获 <c>out</c> 出参。
/// </summary>
/// <param name="As">结果槽名（成员读取可省略）。</param>
/// <param name="Dep">实例调用目标依赖下标（空 = 静态调用，取 <see cref="Target"/>）。</param>
/// <param name="Target">静态调用目标类型全名（实例调用可空）。</param>
/// <param name="Method">方法名 / 属性名 / 字段名。</param>
/// <param name="IsProperty">true = 成员读取（属性优先、回落字段）；false = 方法调用。</param>
/// <param name="Await">true = 结果为 <c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>，折取为同步值。</param>
/// <param name="Expect">期望异常类型全名：命中即捕获（槽 = 异常对象）；未命中原样上抛。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
/// <param name="Out">出参捕获槽名（G2：把方法的 <c>out</c> 实参按位写入该槽）。</param>
/// <param name="Break">true = 本步骤执行后跳出最近一层 <c>foreach</c>。</param>
/// <param name="GenericArgs">开放泛型方法的闭合实参类型全名（W4：按序 <c>MakeGenericMethod</c>）。</param>
/// <param name="Value">调用目标**取值**（W24：对表达式值调用——委托走 <c>DynamicInvoke</c>，实例按其运行时类型解析方法；
/// 与 <see cref="Dep"/> / <see cref="Target"/> 三选一）。</param>
/// <param name="NonPublic">true = 目标方法 / 成员按「公开 + 非公开」解析（W20：产品未公开入口的结构探针）。</param>
public sealed record ScriptedInvokeSpec(
    string? As = null,
    int? Dep = null,
    string? Target = null,
    string Method = "",
    bool IsProperty = false,
    bool Await = false,
    string? Expect = null,
    IReadOnlyList<ScriptedArgSpec>? Args = null,
    string? Out = null,
    bool Break = false,
    IReadOnlyList<string>? GenericArgs = null,
    ScriptedArgSpec? Value = null,
    bool NonPublic = false);

/// <summary>遍历步骤：取集合（槽 / 表达式）逐项执行体内步骤（可过滤）。</summary>
/// <param name="Slot">集合槽名（与 <see cref="Items"/> 二选一）。</param>
/// <param name="Items">集合表达式（与 <see cref="Slot"/> 二选一）。</param>
/// <param name="As">循环变量名（体内 <c>item</c> 源引用它）。</param>
/// <param name="Where">过滤条件表达式（求值为真才进入本次迭代）。</param>
/// <param name="Do">体内步骤序列（声明序 = 执行序）。</param>
public sealed record ScriptedForeachSpec(
    string? Slot = null,
    ScriptedExpr? Items = null,
    string As = "",
    ScriptedExpr? Where = null,
    IReadOnlyList<ScriptedStep>? Do = null);

/// <summary>
/// 临时目录**作用域声明**（P2 / 缺口 G10）：进入 <c>script</c> 前建目录（前缀 + 唯一后缀）→ 按序写预置文件
/// （可 <c>when</c> 门控；相对目录由表达式给出）→ 执行主体 → **正常与异常路径均递归清理**。
/// <para>作用域隔离：每键每次调用独立实例（内层快照口径，防 ANTI-NESTED-POLLUTION）。</para>
/// </summary>
/// <param name="Kind">作用域种类：<c>tempDir</c>（临时目录）/ <c>using</c>（资源释放，W18）/ <c>restore</c>（静态槽还原，W27）。</param>
/// <param name="As">作用域名（值源 <c>tmpPath</c> / <c>tmpDir</c> 据它解析）。</param>
/// <param name="Prefix">目录名前缀（诊断可读；缺省 <c>spec</c>）。</param>
/// <param name="Files">预置文件序列（声明序 = 写盘序）。</param>
/// <param name="Invoke">进入期调用（<c>kind=using</c>：结果写槽 <see cref="As"/>，退出（含异常路径）时若可释放即 <c>Dispose</c>）。</param>
/// <param name="Save">进入期留存项（<c>kind=restore</c>：静态成员当前值 → 槽）。</param>
/// <param name="Open">进入期步骤（<c>kind=restore</c>：声明序 = 执行序；在 <see cref="Save"/> 之后执行）。</param>
/// <param name="Close">退出期步骤（<c>kind=restore</c>：正常与异常路径均执行）。</param>
public sealed record ScriptedScopeSpec(
    string Kind,
    string As,
    string? Prefix = null,
    IReadOnlyList<ScriptedFileSpec>? Files = null,
    ScriptedInvokeSpec? Invoke = null,
    IReadOnlyList<ScriptedSaveSpec>? Save = null,
    IReadOnlyList<ScriptedStep>? Open = null,
    IReadOnlyList<ScriptedStep>? Close = null);

/// <summary>作用域内预置文件（<c>name</c> 不得含路径分隔符；<c>dir</c> 为表达式，不做文本插值方言）。</summary>
/// <param name="Name">文件名（相对 <paramref name="Dir"/>）。</param>
/// <param name="Dir">相对子目录表达式（缺省 = 作用域根；用 <c>concat</c> / <c>var</c> / <c>row</c> 组合路径段）。</param>
/// <param name="From">内容来源（缺省 = 空文件）。</param>
/// <param name="Content">内容表达式（缺省 = <see cref="From"/> 取值的文本形式）。</param>
/// <param name="When">写盘门控（求值为假即跳过该文件）。</param>
public sealed record ScriptedFileSpec(
    string Name,
    ScriptedExpr? Dir = null,
    ScriptedArgSpec? From = null,
    ScriptedExpr? Content = null,
    ScriptedExpr? When = null);

/// <summary>
/// **多场景探针声明**（W2：<c>PortProbeRouter.Declare</c> / <c>.Of</c> 的一等声明等价）：
/// 由 <see cref="Select"/> 取场景名 → 命中则执行该场景的主体（<see cref="Cases"/>）；**未命中即抛**（不静默回落）。
/// <para>每个场景主体是一份独立的脚本绑定声明（同端口键）；基体自身的 <c>script</c> 可为空（主体由场景承载）。</para>
/// </summary>
/// <param name="Select">场景名取值来源（<c>with</c> / <c>arg</c> / <c>var</c> 等）。</param>
/// <param name="Cases">场景名 → 场景主体（键 Ordinal 比较；未命中诊断列全部可用场景名）。</param>
public sealed record ScriptedScenarioSpec(
    ScriptedArgSpec Select,
    IReadOnlyDictionary<string, ScriptedBindingSpec> Cases);

/// <summary>
/// 声明式**脚本绑定**（消除通道 C5）：把「遍历用例声明的数据 + 用上一步结果再调 + 一般取值投影 + 载体形态」
/// 从夹具 C# 变为标准层声明，使编排型端口也能零夹具代码装配。
/// <para>分工红线：本件与 <see cref="ComposedBindingSpec"/> 同为机制层——只认不透明字符串 + 反射 +
/// 取值来源枚举，不含任何领域类型 / 领域谓词（L1 红线）；领域语义由标准文本承载。</para>
/// </summary>
public sealed class ScriptedBindingSpec
{
    public ScriptedBindingSpec(
        string portKey,
        IReadOnlyDictionary<string, ScriptedArgSpec>? binds,
        IReadOnlyList<ComposedDepSpec>? deps,
        IReadOnlyList<ScriptedStep> script,
        IReadOnlyList<ScriptedFieldSpec> project,
        ScriptedDeliverSpec? deliver = null,
        IReadOnlyList<ScriptedScopeSpec>? scopes = null,
        ScriptedScenarioSpec? scenarios = null)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey)
            ? throw new ArgumentException("端口键不能为空", nameof(portKey))
            : portKey;
        Binds = binds ?? new Dictionary<string, ScriptedArgSpec>(StringComparer.Ordinal);
        Deps = deps ?? Array.Empty<ComposedDepSpec>();
        Script = script ?? Array.Empty<ScriptedStep>();
        Project = project ?? Array.Empty<ScriptedFieldSpec>();
        Deliver = deliver ?? new ScriptedDeliverSpec();
        Scopes = scopes ?? Array.Empty<ScriptedScopeSpec>();
        Scenarios = scenarios;
    }

    /// <summary>端口键（诊断定位用）。</summary>
    public string PortKey { get; }

    /// <summary>输入绑定（具名槽 ← 取值来源；声明序 = 求值序）。</summary>
    public IReadOnlyDictionary<string, ScriptedArgSpec> Binds { get; }

    /// <summary>依赖序列（声明序 = 构建序；构建语义单源 = <see cref="ComposedDeps"/>）。</summary>
    public IReadOnlyList<ComposedDepSpec> Deps { get; }

    /// <summary>脚本步骤（声明序 = 执行序）。</summary>
    public IReadOnlyList<ScriptedStep> Script { get; }

    /// <summary>投影序列（声明序 = 产物字段序）。</summary>
    public IReadOnlyList<ScriptedFieldSpec> Project { get; }

    /// <summary>载体开关（延迟动作 / 问题清单 / 延迟动作步骤）。</summary>
    public ScriptedDeliverSpec Deliver { get; }

    /// <summary>临时目录作用域序列（进入脚本前建 / 写盘，退出（含异常）递归清理）。</summary>
    public IReadOnlyList<ScriptedScopeSpec> Scopes { get; }

    /// <summary>多场景探针声明（W2；非空时主体交由命中的场景承载，见 <see cref="Scenarios"/>）。</summary>
    public ScriptedScenarioSpec? Scenarios { get; }

    /// <summary>数据半注入的工厂表（<c>deps.factory</c> 用；缺表且声明了 factory 即 fail-fast）。</summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories { get; init; } =
        new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);
}

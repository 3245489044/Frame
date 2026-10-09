namespace DataCenter.Protocol.Standards;

/// <summary>
/// 脚本步骤（标准框架通用化重构 §2.2 第 7 项；对应内核 <c>ScriptedStep</c>）：
/// 形态恰一个（<c>new</c> / <c>invoke</c> / <c>foreach</c> / <c>throw</c>），<see cref="Collects"/> / <see cref="When"/> 为步骤修饰。
/// </summary>
/// <param name="Collects">收集声明（步产物 / 槽值 → 收集槽）。</param>
/// <param name="When">条件短路表达式（求值为真才执行本步骤）。</param>
/// <param name="New">构造步骤（判别式）。</param>
/// <param name="Invoke">调用步骤（判别式）。</param>
/// <param name="Foreach">遍历步骤（判别式）。</param>
/// <param name="Throw">主动抛出步骤（判别式；G3）。</param>
/// <param name="Source">纯值源步骤（判别式；集合字面量 <c>from:list</c> / <c>from:map</c>）：求值结果写入 <see cref="As"/> 槽。</param>
/// <param name="Expr">表达式步骤（判别式；通用 <c>ExprStandard</c> 算子，如 <c>filter</c>/<c>select</c>/<c>dict</c>）：求值结果写入 <see cref="As"/> 槽。</param>
/// <param name="As">值源 / 表达式步骤的写槽名（仅 <see cref="Source"/> / <see cref="Expr"/> 形态用）。</param>
public sealed record StepStandard(
    IReadOnlyList<CollectStandard>? Collects = null,
    ExprStandard? When = null,
    NewStandard? New = null,
    InvokeStandard? Invoke = null,
    ForeachStandard? Foreach = null,
    ThrowStandard? Throw = null,
    ValueSourceStandard? Source = null,
    ExprStandard? Expr = null,
    string? As = null);

/// <summary>
/// 主动抛出步骤（<c>script[].throw</c>；G3）：与 <see cref="NewStandard.Expect"/>（构造期）/ 
/// <see cref="InvokeStandard.Expect"/>（调用期）构成判据三件套的「主动」一支。
/// </summary>
/// <param name="Type">异常类型全名（须可解析且为 <see cref="System.Exception"/> 子类，否则即抛）。</param>
/// <param name="Message">异常消息（可空；给定则类型须有 <c>(string)</c> 构造，否则即抛）。</param>
public sealed record ThrowStandard(string Type = "", string? Message = null);

/// <summary>
/// 调用步骤（标准框架通用化重构 §2.2 第 8 项；对应内核 <c>ScriptedInvokeSpec</c>）：
/// 方法调用 / 成员读取（静态或对依赖实例），可异步折取、可期望异常、可捕获 <c>out</c> 出参。
/// </summary>
/// <param name="As">结果槽名（成员读取可省略）。</param>
/// <param name="Dep">实例调用目标依赖下标（空 = 静态调用，取 <see cref="Target"/>）。</param>
/// <param name="Target">静态调用目标类型全名（实例调用可空）。</param>
/// <param name="Method">方法名 / 属性名 / 字段名。</param>
/// <param name="IsProperty">true = 成员读取（属性优先、回落字段）；false = 方法调用。</param>
/// <param name="Await">true = 结果为 <c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>，折取为同步值。</param>
/// <param name="Expect">期望异常类型全名：命中即捕获（槽 = 异常对象）；未命中原样上抛。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
/// <param name="Out">出参捕获槽名（把方法的 <c>out</c> 实参按位写入该槽）。</param>
/// <param name="Break">true = 本步骤执行后跳出最近一层 <c>foreach</c>。</param>
/// <param name="GenericArgs">开放泛型方法的闭合实参类型全名。</param>
/// <param name="Value">调用目标取值（对表达式值调用；与 <see cref="Dep"/> / <see cref="Target"/> 三选一）。</param>
/// <param name="NonPublic">true = 目标方法 / 成员按「公开 + 非公开」解析。</param>
/// <param name="KeepTask">true = **不折取**任务结果（D2）：<c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>
/// 作为**值**进入结果槽（供「Task 当数据传」的语义，如把任务交给下游编排 / <c>Task.WhenAll</c>）；
/// 缺省 false = 折取为同步值（<see cref="Await"/> 的原义）。与 <see cref="Await"/>=true 同声明即 fail-fast（二义）。</param>
public sealed record InvokeStandard(
    string? As = null,
    int? Dep = null,
    string? Target = null,
    string Method = "",
    bool IsProperty = false,
    bool Await = false,
    string? Expect = null,
    IReadOnlyList<ValueSourceStandard>? Args = null,
    string? Out = null,
    bool Break = false,
    IReadOnlyList<string>? GenericArgs = null,
    ValueSourceStandard? Value = null,
    bool NonPublic = false,
    bool KeepTask = false);

/// <summary>
/// 遍历步骤（标准框架通用化重构 §2.2 第 9 项；对应内核 <c>ScriptedForeachSpec</c>）：
/// 取集合（槽 / 表达式）逐项执行体内步骤（可过滤）。
/// </summary>
/// <param name="Slot">集合槽名（与 <see cref="Items"/> 二选一）。</param>
/// <param name="Items">集合表达式（与 <see cref="Slot"/> 二选一）。</param>
/// <param name="As">循环变量名（体内 <c>item</c> 源引用它）。</param>
/// <param name="Where">过滤条件表达式（求值为真才进入本次迭代）。</param>
/// <param name="Do">体内步骤序列（声明序 = 执行序）。</param>
public sealed record ForeachStandard(
    string? Slot = null,
    ExprStandard? Items = null,
    string As = "",
    ExprStandard? Where = null,
    IReadOnlyList<StepStandard>? Do = null);

/// <summary>
/// 构造步骤（标准框架通用化重构 §2.2 第 10 项；对应内核 <c>ScriptedNewSpec</c>）：
/// 反射构造一个对象并写入槽（等价 <c>deps.new</c>，但可在遍历体内构造）。
/// </summary>
/// <param name="As">结果槽名。</param>
/// <param name="Type">类型全名（开放泛型形如 <c>Ns.Type`1</c>）。</param>
/// <param name="GenericArgs">闭合泛型实参类型全名。</param>
/// <param name="Args">构造实参声明。</param>
/// <param name="Chain">链式后置调用（构造后按序调用实例方法）。</param>
/// <param name="Init">构造后属性 / 字段初始化（声明序 = 赋值序）。</param>
/// <param name="Expect">构造期期望异常类型全名（<c>|</c> 分隔多类型；解析单源 = <c>ScriptedInvokeRunner.ExpectTypes</c>）：命中 ⇒ 结果为该异常对象（<see cref="Chain"/> / <see cref="Init"/> 不再施加），
/// 未命中 / 未抛 ⇒ 结果为构造实例（口径与 <see cref="InvokeStandard.Expect"/> 同源，单源实现 = <c>PortInvocation.ExecuteAny</c>）。</param>
public sealed record NewStandard(
    string As,
    string Type,
    IReadOnlyList<string>? GenericArgs = null,
    IReadOnlyList<ValueSourceStandard>? Args = null,
    IReadOnlyList<ChainStandard>? Chain = null,
    IReadOnlyList<InitStandard>? Init = null,
    string? Expect = null);

/// <summary>
/// 属性 / 字段初始化项（标准框架通用化重构 §2.2 第 11 项；对应内核 <c>ScriptedInitSpec</c>）：
/// 只做「构造后赋值」这一机制，属性与字段不区分。
/// </summary>
/// <param name="Member">成员名（属性优先，回落字段）。</param>
/// <param name="Value">取值声明。</param>
/// <param name="Key">
/// 容器写入键（G7；可空）：给出时 <see cref="Member"/> 取值须为**可写索引容器**
/// （字典 / 列表 / 具索引器者），写入 <c>member[key] = value</c>；空 = 按属性 / 字段整体赋值。
/// </param>
public sealed record InitStandard(
    string Member,
    ValueSourceStandard? Value = null,
    ValueSourceStandard? Key = null);

/// <summary>
/// 链式后置调用（标准框架通用化重构 §2.2 第 12 项；对应内核 <c>ScriptedChainSpec</c>）：
/// 返回值默认忽略（实例即链值）；<see cref="Rebind"/> = true 时以返回值替换链值。
/// </summary>
/// <param name="Method">实例方法名。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
/// <param name="Rebind">true = 以本调用的返回值替换链值。</param>
public sealed record ChainStandard(
    string Method,
    IReadOnlyList<ValueSourceStandard>? Args = null,
    bool Rebind = false);

/// <summary>
/// 委托工厂声明（标准框架通用化重构 §2.2 第 13 项；对应内核 <c>ScriptedDelegateSpec</c>）：
/// 按 <see cref="Kind"/> 闭合委托类型后构造委托，其调用体在委托被调用时执行 <see cref="Steps"/>。
/// </summary>
/// <param name="Kind">委托类型全名。</param>
/// <param name="Steps">调用期执行的步骤序列（声明序 = 执行序）。</param>
/// <param name="Result">返回值表达式（缺省 = 末步骤产物）。</param>
/// <param name="Parameters">形参帧名序列（缺省 = <c>arg0</c>/<c>arg1</c>/…）。</param>
/// <param name="Collect">调用留痕收集槽（非空时每次调用把实参数组追加到该槽）。</param>
/// <param name="Await">true = 体产物为任务时折取。</param>
public sealed record DelegateStandard(
    string? Kind = null,
    IReadOnlyList<StepStandard>? Steps = null,
    ExprStandard? Result = null,
    IReadOnlyList<string>? Parameters = null,
    string? Collect = null,
    bool Await = false);

/// <summary>
/// 进入期取值留存项（标准框架通用化重构 §2.2 第 14 项；对应内核 <c>ScriptedSaveSpec</c>）：
/// 把静态成员当前值读入槽，供退出期写回（<c>restore</c> 作用域用）。
/// </summary>
/// <param name="Slot">留存槽名。</param>
/// <param name="Type">成员所属类型全名。</param>
/// <param name="Key">成员名（属性优先、回落字段）。</param>
/// <param name="GenericArgs">类型泛型闭合实参（开放泛型类型用）。</param>
/// <param name="Set">进入期设值来源（NG-2；缺省 = 只留存不设值，行为同 W27）。</param>
/// <param name="Restore">退出期自动还原（NG-2；true = 把 <see cref="Slot"/> 留存值写回成员，
/// 含异常路径；缺省 false = 只留存，由 <see cref="ScopeStandard.Close"/> 步骤显式还原）。</param>
public sealed record SaveStandard(
    string Slot,
    string Type,
    string Key,
    IReadOnlyList<string>? GenericArgs = null,
    ValueSourceStandard? Set = null,
    bool Restore = false);

/// <summary>
/// 收集声明（标准框架通用化重构 §2.2 第 15 项；对应内核 <c>ScriptedCollectSpec</c>）：
/// 把本步骤产物或指定槽的当前值按序追加到收集槽。
/// </summary>
/// <param name="Slot">收集槽名。</param>
/// <param name="From">收集源槽名（空 = 本步骤产物）。</param>
public sealed record CollectStandard(string Slot, string? From = null);

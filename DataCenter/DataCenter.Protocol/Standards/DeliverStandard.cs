namespace DataCenter.Protocol.Standards;

/// <summary>
/// 载体声明（标准框架通用化重构 §2.2 第 21 项；对应内核 <c>ScriptedDeliverSpec</c>）：
/// <c>kind=value</c>（常规产物，缺省）/ <c>issues</c>（问题清单，由 <see cref="Capture"/> 产出）/
/// <c>action</c>（延迟动作，由 <see cref="DeferSteps"/> 或整脚本包装为断言期求值）。
/// <para>兼容：<see cref="Defer"/> / <see cref="Issues"/> 布尔为旧形态，等价于对应 <c>kind</c>。</para>
/// </summary>
/// <param name="Kind">载体种类（取值域见 <see cref="PortImplVocabulary.DeliverKinds"/>；null = 按旧布尔推断）。</param>
/// <param name="Defer">延迟动作载体（整脚本包装；旧形态）。</param>
/// <param name="Issues">问题清单载体（旧形态；清单来源由 <see cref="IssueField"/> 指定）。</param>
/// <param name="IssueField">清单来源字段 / 收集槽名（空 = 取投影首字段）。</param>
/// <param name="Capture">issue 承载捕获（<c>kind=issues</c> 的主力形态）。</param>
/// <param name="DeferSteps">延迟动作步骤（<c>kind=action</c>：断言期才执行这些步骤并取投影）。</param>
/// <param name="Value">值载体表达式（<c>kind=value</c>：脚本执行后取该表达式结果为产物）。</param>
public sealed record DeliverStandard(
    string? Kind = null,
    bool Defer = false,
    bool Issues = false,
    string? IssueField = null,
    DeliverCaptureStandard? Capture = null,
    IReadOnlyList<StepStandard>? DeferSteps = null,
    ExprStandard? Value = null);

/// <summary>
/// issue 承载捕获（标准框架通用化重构 §2.2 第 22 项；对应内核 <c>ScriptedDeliverCaptureSpec</c>）：
/// 调用体 + 期望异常类型集 → 不命中期望即原样上抛；命中则产**一条** issue；未抛 → 空清单。
/// </summary>
/// <param name="Call">捕获调用（<c>invoke</c> 声明；<c>expect</c> 由本件按 <see cref="Expect"/> 施加）。</param>
/// <param name="Expect">期望异常类型全名集（空 = 不捕获任何异常，即恒空清单）。</param>
/// <param name="Message">消息口径：<c>Message</c>（缺省）/ <c>TypeName</c>。</param>
/// <param name="MessageField">消息来源槽名（缺省 = 捕获到的异常对象）。</param>
/// <param name="When">门控表达式（求值为假即跳过捕获，恒空清单）。</param>
public sealed record DeliverCaptureStandard(
    InvokeStandard Call,
    IReadOnlyList<string> Expect,
    string? Message = null,
    string? MessageField = null,
    ExprStandard? When = null);

/// <summary>
/// 多场景探针声明（标准框架通用化重构 §2.2 第 23 项；对应内核 <c>ScriptedScenarioSpec</c>）：
/// 由 <see cref="Select"/> 取场景名 → 命中则执行该场景主体（<see cref="Cases"/>）；未命中即抛。
/// </summary>
/// <param name="Select">场景名取值来源。</param>
/// <param name="Cases">场景名 → 场景主体（键 Ordinal 比较；场景名为不透明名，故为开放映射）。</param>
public sealed record ScenarioStandard(
    ValueSourceStandard Select,
    IReadOnlyDictionary<string, ScenarioBodyStandard> Cases);

/// <summary>
/// 场景主体（标准框架通用化重构 §2.2 第 23 项的自引用形态；对应内核场景 <c>ScriptedBindingSpec</c>）：
/// 一份独立的脚本声明体（同端口键），承载该场景自己的取值 / 依赖 / 步骤 / 投影 / 载体 / 作用域。
/// </summary>
/// <param name="Binds">输入绑定（槽名 → 取值来源）。</param>
/// <param name="Deps">依赖声明（声明序 = 构建序）。</param>
/// <param name="Script">步骤声明（声明序 = 执行序）。</param>
/// <param name="Project">投影声明（字段序 = 产物序）。</param>
/// <param name="Deliver">载体声明。</param>
/// <param name="Scopes">作用域声明。</param>
public sealed record ScenarioBodyStandard(
    IReadOnlyDictionary<string, ValueSourceStandard>? Binds = null,
    IReadOnlyList<DepStandard>? Deps = null,
    IReadOnlyList<StepStandard>? Script = null,
    IReadOnlyList<FieldExprStandard>? Project = null,
    DeliverStandard? Deliver = null,
    IReadOnlyList<ScopeStandard>? Scopes = null);

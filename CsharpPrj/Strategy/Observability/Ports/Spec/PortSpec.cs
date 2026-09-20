namespace Strategy.Observability.Ports;

using System.Text.Json;
using DataCenter.Protocol.Standards;

/// <summary>
/// 端口指令操作码（R2-2b）：三通道差异压平为**指令序列**的判别键。
/// <para>
/// 8 个操作码**已全部落地**（求值分派点 = <see cref="PortEvaluator"/> 的 <c>switch (Op)</c>）；
/// 形态残缺的指令（操作数为空或类型不符）在求值期 fail-fast（不静默返回空），见 <c>PortEvaluator.Operand</c>。
/// </para>
/// </summary>
public enum PortOp
{
    /// <summary>调用（静态方法 / 成员读取；本批唯一实现的操作码）。</summary>
    Call,

    /// <summary>依赖构建（R3-2）。</summary>
    Dep,

    /// <summary>构造（R3-2）。</summary>
    New,

    /// <summary>投影（R3-2）。</summary>
    Project,

    /// <summary>遍历（R3-3）。</summary>
    Loop,

    /// <summary>行表值源（R3-3）。</summary>
    Table,

    /// <summary>词元值源（R3-3）。</summary>
    Presence,

    /// <summary>收集（R3-3）。</summary>
    Collect,
}

/// <summary>
/// 端口指令（R2-2b）：**SSA 形式** —— 每条指令一个结果写槽（<see cref="Out"/>），操作数经
/// <see cref="Args"/> 承载唯一转换表视图（<see cref="PortValueOptions"/> 经标准单源 <see cref="PortArgStandard"/> 投影）。
/// <para>机制件：只认不透明字符串（类型全名 / 方法名）+ 结构算子，零领域语义。</para>
/// </summary>
/// <param name="Op">操作码（判别键）。</param>
/// <param name="Out">写槽名（空 = 直出；异常诊断定位用）。</param>
/// <param name="Target">目标类型全名（静态调用 / 构造）。</param>
/// <param name="Method">目标成员名（方法 / 属性 / 字段）。</param>
/// <param name="Dep">依赖下标（实例调用）。</param>
/// <param name="Args">实参声明（位序即语义）。</param>
/// <param name="Value">常量 / 内联结构。</param>
/// <param name="Operand">
/// 指令操作数（**按 <see cref="Op"/> 解释**）：承载 ① 标准声明记录（<c>DataCenter.Protocol.Standards</c> 的
/// <c>*Standard</c>）—— 声明面是唯一数据源，IR 只做**引用**，不另立同形副本（<c>Dep</c>→<c>DepStandard</c>、
/// <c>New</c>→<c>NewStandard</c>、<c>Loop</c>→<c>ForeachStandard</c>、<c>Table</c>/<c>Presence</c>→<c>ValueSourceStandard</c>、
/// <c>Project</c>→<c>ComposedFieldStandard</c>/<c>FieldExprStandard</c>）。形状不符即 fail-fast（不静默）。
/// </param>
/// <param name="Body">嵌套指令块（<c>Loop</c> 的循环体；空 = 无体）。声明序 = 执行序。</param>
/// <param name="When">
/// 步骤条件短路（<c>when</c> 表达式；null = 无条件）。求值为假即跳过本指令；
/// 求值为 ⊥（缺槽 / 解析失败）→ **不执行且留痕**（不静默）。
/// </param>
/// <param name="Collects">
/// 步骤收集声明（步产物 / 指定槽当前值 → 收集槽；空 = 不收集）。**步骤修饰**，与指令同体承载
/// （不另立指令），执行序 = 主体执行后按声明序收集。
/// </param>
public sealed record PortInstr(
    PortOp Op,
    string? Out = null,
    string? Target = null,
    string? Method = null,
    int? Dep = null,
    IReadOnlyList<PortArgStandard>? Args = null,
    JsonElement? Value = null,
    object? Operand = null,
    IReadOnlyList<PortInstr>? Body = null,
    ExprStandard? When = null,
    IReadOnlyList<CollectStandard>? Collects = null);

/// <summary>
/// 实现归属（R2-2b）：规格的落地点 + **只读驱动元数据**（<see cref="LegacyKind"/> / <see cref="ProductFields"/>）。
/// <para>
/// 纪律：驱动元数据供上层装配与守卫消费，**求值不得读**（见 <see cref="PortLedgerEntry"/> 同名说明）。
/// </para>
/// </summary>
/// <param name="Kind">绑定方式（只读分类信息：<c>reflective</c> / <c>composed</c> / <c>scripted</c> / <c>delegate</c> / <c>frozen</c>）。</param>
/// <param name="Id">委派实现登记 id。</param>
/// <param name="Target">目标类型全名。</param>
/// <param name="Method">目标方法名。</param>
/// <param name="Reason">冻结 / 未绑定原因。</param>
/// <param name="LegacyKind">spec 面入口名（历史 kind；只读驱动元数据）。</param>
/// <param name="ProductFields">产物字段声明（只读驱动元数据）。</param>
public sealed record PortImplRef(
    string Kind,
    string? Id = null,
    string? Target = null,
    string? Method = null,
    string? Reason = null,
    string? LegacyKind = null,
    string ProductFields = "");

/// <summary>
/// 端口求值规格（R2-2b，唯一规格模型）：声明视图（<see cref="PortLedgerEntry"/>）的**单向形态** ——
/// 形参表 + 指令序列 + 实现归属。
/// <para>
/// 求值面上 <see cref="Program"/> 是唯一执行载体（全族一视同仁）；<see cref="IsEvaluable"/> 为**装配期择路判据**
/// —— 签名面已编译为指令者为 true（走求值引擎），否则为 false（由旧绑定类给既有诊断、记 Unbound）。
/// 本判据随签名面编译入指令一并退场（批 2）。
/// </para>
/// </summary>
/// <param name="PortKey">端口键（不透明字符串）。</param>
/// <param name="Impl">实现归属（含只读驱动元数据）。</param>
/// <param name="Params">形参表（位序即语义；R2-1 <see cref="PortParam"/>）。</param>
/// <param name="Program">指令序列（声明序 = 执行序）。</param>
/// <param name="IsEvaluable">是否已可经求值引擎执行（Program 非空且该族已迁移）。</param>
/// <param name="Deliver">
/// 载体声明（脚本族；① 标准记录 <c>DeliverStandard</c>；null = 常规值载体）。承载「问题清单 / 延迟动作」两态，
/// 由求值引擎在产物投影后按声明包装（J2 <c>Project</c>/载体路径承接）。
/// </param>
/// <param name="Binds">输入绑定（槽名 → 取值来源；脚本族签名面，① 记录引用）。</param>
/// <param name="Deps">依赖声明（声明序 = 构建序；脚本族签名面，① 记录引用）。</param>
/// <param name="ScriptProject">脚本投影声明（字段 ← 表达式；脚本族签名面，① 记录引用）。</param>
/// <param name="Scopes">作用域声明（tempDir / using / restore；脚本族签名面，① 记录引用）。</param>
/// <param name="Scenarios">多场景探针声明（声明了才非空；脚本族签名面，① 记录引用）。</param>
/// <remarks>
/// 签名面（<see cref="Deliver"/> / <see cref="Binds"/> / <see cref="Deps"/> / <see cref="ScriptProject"/> /
/// <see cref="Scopes"/> / <see cref="Scenarios"/>）一律为 ① 标准记录的**引用**（零运行期成员、零同形副本）；
/// 缺省 null = 无该面（reflective / composed 即如此，其可执行面全在 <see cref="Program"/>）。
/// </remarks>
public sealed record PortSpec(
    string PortKey,
    PortImplRef Impl,
    IReadOnlyList<PortParam> Params,
    IReadOnlyList<PortInstr> Program,
    bool IsEvaluable,
    DeliverStandard? Deliver = null,
    IReadOnlyDictionary<string, ValueSourceStandard>? Binds = null,
    IReadOnlyList<DepStandard>? Deps = null,
    IReadOnlyList<FieldExprStandard>? ScriptProject = null,
    IReadOnlyList<ScopeStandard>? Scopes = null,
    ScenarioStandard? Scenarios = null);

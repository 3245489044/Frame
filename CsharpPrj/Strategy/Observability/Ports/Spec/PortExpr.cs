namespace Strategy.Observability.Ports;

using System.Text.Json;


/// <summary>
/// 投影**取值表达式**（取代固定取值法枚举；递归可组合）：<see cref="Source"/> 取源值，
/// 其余成员为算子（每次恰好一个）。语义表见《AgentTest 端口声明式编排框架-设计规范》§5。
/// <para>分工：本件为机制件——表达式成员是纯结构算子（计数 / 拼接 / 包含 / 全称 / 存在 / 展开 / 相等），
/// 不含任何领域谓词；<c>call</c> 谓词的语义由声明里的静态方法名承载（不透明字符串）。</para>
/// </summary>
/// <param name="Source">源值（槽 / 实参 / 遍历变量 / 依赖 / 常量；可带属性路径）。</param>
/// <param name="Path">属性路径（与 <see cref="Collect"/> 连用：对收集元素逐项取路径）。</param>
/// <param name="Count">计数（集合 / 数组 / 字符串；null → 0）。</param>
/// <param name="NotNull">非空布尔。</param>
/// <param name="AsText">文本形式。</param>
/// <param name="TypeName">运行时类型名（异常 / <see cref="Type"/> / 一般值）。</param>
/// <param name="Message">异常消息。</param>
/// <param name="Thrown">是否为异常对象（期望异常命中）。</param>
/// <param name="Concat">片段按序拼接（null → 空串）。</param>
/// <param name="Separator">拼接分隔符（缺省空串）。</param>
/// <param name="Contains">被查集合 / 文本。</param>
/// <param name="Of">被含元素 / 子串 / 字典键。</param>
/// <param name="All">全称判定：遍历该表达式的元素。</param>
/// <param name="Any">存在判定：遍历该表达式的元素。</param>
/// <param name="AllIn">全称 / 存在的判据：元素 ∈ 该集合（与 <see cref="Call"/> 二选一）。</param>
/// <param name="Call">判据谓词方法名（静态；对每个元素求值取布尔）。</param>
/// <param name="CallTarget">判据谓词的静态类型全名。</param>
/// <param name="Spread">动态键展开（对象属性 / 字典逐项写入产物）。</param>
/// <param name="Except">展开时排除的键。</param>
/// <param name="CountEqual">两次取值计数相等（恰两个操作数）。</param>
/// <param name="Collect">收集槽名（对收集元素逐项投影）。</param>
/// <param name="CollectText">收集元素逐项取文本。</param>
/// <param name="SameAsLast">收集元素与另一收集槽末位引用相等（逐项布尔）。</param>
/// <param name="IsNull">空判定（null → true）。</param>
/// <param name="Not">逻辑非（真值取反；<c>⊥</c> 保持 <c>⊥</c>）。</param>
/// <param name="Eq">相等比较（恰两个操作数：<c>[左, 右]</c>）。</param>
/// <param name="Ne">不等比较（恰两个操作数）。</param>
/// <param name="Gt">大于比较（恰两个操作数；数值序）。</param>
/// <param name="Ge">大于等于比较（恰两个操作数）。</param>
/// <param name="Lt">小于比较（恰两个操作数）。</param>
/// <param name="Le">小于等于比较（恰两个操作数）。</param>
/// <param name="AllOf">逻辑合取（子表达式列表；任一为假即假，遇 <c>⊥</c> 短路为 <c>⊥</c>）。</param>
/// <param name="AnyOf">逻辑析取（子表达式列表；任一为真即真，遇 <c>⊥</c> 短路为 <c>⊥</c>）。</param>
/// <param name="Agg">聚合来源（W3：遍历该表达式的元素后按 <see cref="AggOp"/> 聚合）。</param>
/// <param name="AggOp">聚合算子：<c>sum</c>（缺省） / <c>max</c> / <c>min</c> / <c>avg</c>（后三者空集即抛）。</param>
/// <param name="AggPath">聚合元素的取值路径（缺省 = 元素本身）。</param>
/// <param name="SameReference">引用同一性比较（W5：恰两个操作数，按 <c>ReferenceEquals</c> 判定）。</param>
/// <param name="Filter">筛选来源（W8：遍历该表达式的元素，按 <see cref="FilterWhere"/> 保留后成集）。</param>
/// <param name="FilterWhere">保留条件（体内 <c>from:row</c> 即当前元素；缺省 = 元素非空）。</param>
/// <param name="FilterPath">保留元素的取值路径（缺省 = 元素本身）。</param>
/// <param name="FirstOrNull">首命中算子的未命中口径（<c>first.orNull</c>：true = 未命中取 null；缺省 = 未命中即抛）。</param>
/// <param name="Last">末命中来源（<c>last{over,where,path,orNull}</c>：从尾端起首个命中项；体内 <c>from:row</c> 即当前元素）。</param>
/// <param name="LastWhere">末命中的保留条件（缺省 = 元素非空）。</param>
/// <param name="LastPath">末命中项的取值路径（缺省 = 元素本身）。</param>
/// <param name="LastOrNull">空集口径（true = 取 null；缺省同样取 null，此标志供声明自描述）。</param>
/// <param name="Dict">键值表装配来源（<c>dict{over,key,value,where,onDuplicate}</c>；空集 = 空字典；键重复缺省 fail-fast、<c>onDuplicate:"last"</c> 覆盖）。</param>
/// <param name="DictKey">表键表达式（体内 <c>from:row</c> 即当前元素；求值文本为空 → 跳过该项，不可作键）。</param>
/// <param name="DictValue">表值表达式（体内 <c>from:row</c> 即当前元素；缺省 = 元素本身）。</param>
/// <param name="DictWhere">表项门控（求值为假即跳过该项）。</param>
/// <param name="DictOnDuplicate">键重复处置（缺省 <c>fail</c> = 即抛，对齐夹具 <c>TryAdd</c>；<c>last</c> = 后者覆盖）。</param>
/// <param name="Invoke">调用算子（表达式内调用：执行声明调用并取其结果；<c>as</c> / <c>collect</c> 不参与）。</param>
public sealed record ScriptedExpr(
    ScriptedArgSpec? Source = null,
    string? Path = null,
    ScriptedExpr? Count = null,
    ScriptedExpr? NotNull = null,
    ScriptedExpr? AsText = null,
    ScriptedExpr? TypeName = null,
    ScriptedExpr? Message = null,
    ScriptedExpr? Thrown = null,
    IReadOnlyList<ScriptedExpr>? Concat = null,
    string? Separator = null,
    ScriptedExpr? Contains = null,
    ScriptedExpr? Of = null,
    ScriptedExpr? All = null,
    ScriptedExpr? Any = null,
    ScriptedExpr? AllIn = null,
    string? Call = null,
    string? CallTarget = null,
    ScriptedExpr? Spread = null,
    IReadOnlyList<string>? Except = null,
    IReadOnlyList<ScriptedExpr>? CountEqual = null,
    string? Collect = null,
    bool CollectText = false,
    string? SameAsLast = null,
    ScriptedExpr? IsNull = null,
    ScriptedExpr? Not = null,
    IReadOnlyList<ScriptedExpr>? Eq = null,
    IReadOnlyList<ScriptedExpr>? Ne = null,
    IReadOnlyList<ScriptedExpr>? Gt = null,
    IReadOnlyList<ScriptedExpr>? Ge = null,
    IReadOnlyList<ScriptedExpr>? Lt = null,
    IReadOnlyList<ScriptedExpr>? Le = null,
    IReadOnlyList<ScriptedExpr>? AllOf = null,
    IReadOnlyList<ScriptedExpr>? AnyOf = null,
    ScriptedInvokeSpec? Invoke = null,
    ScriptedExpr? First = null,
    ScriptedExpr? FirstWhere = null,
    string? FirstPath = null,
    ScriptedExpr? Agg = null,
    string? AggOp = null,
    string? AggPath = null,
    ScriptedExpr? Filter = null,
    ScriptedExpr? FilterWhere = null,
    string? FilterPath = null,
    IReadOnlyList<ScriptedExpr>? SameReference = null,
    IReadOnlyList<ScriptedExpr>? Coalesce = null,
    ScriptedExpr? Dict = null,
    ScriptedExpr? DictKey = null,
    ScriptedExpr? DictValue = null,
    ScriptedExpr? DictWhere = null,
    string? DictOnDuplicate = null,
    ScriptedExpr? Last = null,
    ScriptedExpr? LastWhere = null,
    string? LastPath = null,
    bool FirstOrNull = false,
    bool LastOrNull = false,
    IReadOnlyList<ScriptedConcatPartSpec>? Parts = null,
    ScriptedExpr? Distinct = null,
    IReadOnlyList<ScriptedExpr>? SequenceEqual = null,
    ScriptedExpr? Fold = null,
    string? FoldAs = null,
    ScriptedArgSpec? FoldInit = null,
    ScriptedChainSpec? FoldStep = null);

/// <summary>
/// 拼接**段**（W19 文本模板）：<see cref="Expr"/> 的文本形式按序拼接；<see cref="When"/> 求值为假即**省略该段**
/// （条件行省略：模板里「有则一行、无则整行消失」的声明等价）。
/// </summary>
/// <param name="Expr">段表达式（其文本形式为段文本）。</param>
/// <param name="When">段门控（缺省 = 恒参与）。</param>
public sealed record ScriptedConcatPartSpec(ScriptedExpr Expr, ScriptedExpr? When = null);

/// <summary>产物字段 ← 投影表达式（字段序 = 产物序；字段名须与契约声明逐字同序）。</summary>
/// <param name="Field">产物字段名。</param>
/// <param name="Expr">取值表达式。</param>
public sealed record ScriptedFieldSpec(string Field, ScriptedExpr Expr);

/// <summary>
/// 载体形态（L0 载体的声明面表达，P4）：<c>kind=value</c>（常规产物，缺省）/ <c>issues</c>（问题清单，
/// 由 <see cref="Capture"/> 的调用 + 期望异常产出）/ <c>action</c>（延迟动作，由 <see cref="DeferSteps"/> 或
/// 整个脚本包装为断言期求值）。
/// <para>兼容：<see cref="Defer"/> / <see cref="Issues"/> 布尔为旧形态，等价于对应 <c>kind</c>。</para>
/// </summary>
/// <param name="Kind">载体种类：<c>value</c> / <c>issues</c> / <c>action</c>（null = 按旧布尔推断）。</param>
/// <param name="Defer">延迟动作载体（整脚本包装；旧形态）。</param>
/// <param name="Issues">问题清单载体（旧形态；清单来源由 <see cref="IssueField"/> 指定）。</param>
/// <param name="IssueField">清单来源字段 / 收集槽名（空 = 取投影首字段）。</param>
/// <param name="Capture">issue 承载捕获（<c>kind=issues</c> 的主力形态）。</param>
/// <param name="DeferSteps">延迟动作步骤（<c>kind=action</c>：断言期才执行这些步骤并取投影）。</param>
/// <param name="Value">值载体表达式（<c>kind=value</c>：脚本执行后取该表达式结果为产物——原始标量 / 文本 / 字典，
/// 用于「声明面产不出具名字段」的键，如分支序列化文本、动态键映射；与 <see cref="Capture"/> / <see cref="DeferSteps"/> 互斥）。</param>
public sealed record ScriptedDeliverSpec(
    string? Kind = null,
    bool Defer = false,
    bool Issues = false,
    string? IssueField = null,
    ScriptedDeliverCaptureSpec? Capture = null,
    IReadOnlyList<ScriptedStep>? DeferSteps = null,
    ScriptedExpr? Value = null);

/// <summary>
/// issue 承载捕获（P4）：调用体 + 期望异常类型集 → 不命中期望即原样上抛；命中则产**一条** issue；
/// 未抛 → **空清单**（语义与 L0 <c>OutcomeIssues.Capture</c> 一致）。
/// </summary>
/// <param name="Call">捕获调用（<c>invoke</c> 声明；<c>expect</c> 由本件按 <see cref="Expect"/> 施加）。</param>
/// <param name="Expect">期望异常类型全名集（空 = 不捕获任何异常，即恒空清单）。</param>
/// <param name="Message">消息口径：<c>Message</c>（缺省）/ <c>TypeName</c>。</param>
/// <param name="MessageField">消息来源槽名（缺省 = 捕获到的异常对象）。</param>
/// <param name="When">门控表达式（求值为假即跳过捕获，恒空清单）。</param>
public sealed record ScriptedDeliverCaptureSpec(
    ScriptedInvokeSpec Call,
    IReadOnlyList<string> Expect,
    string? Message = null,
    string? MessageField = null,
    ScriptedExpr? When = null);

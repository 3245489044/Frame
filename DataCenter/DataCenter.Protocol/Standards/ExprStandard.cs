namespace DataCenter.Protocol.Standards;

/// <summary>
/// 投影取值表达式（标准框架通用化重构 §2.2 第 19 项；对应内核 <c>ScriptedExpr</c>）：
/// <see cref="Source"/> 取源值，其余成员为算子（每次恰好一个）；递归可组合。
/// <para>
/// 分工：表达式成员是纯结构算子（计数 / 拼接 / 包含 / 全称 / 存在 / 展开 / 相等 / 折叠），不含领域谓词——
/// <c>call</c> 谓词的语义由声明里的静态方法名（不透明字符串）承载；算子名与 JSON 键的对应见
/// <see cref="PortImplVocabulary.ExprOperatorKeys"/>。
/// </para>
/// </summary>
/// <param name="Source">源值（槽 / 实参 / 遍历变量 / 依赖 / 常量；可带属性路径）。</param>
/// <param name="Path">属性路径（与 <see cref="Collect"/> 连用：对收集元素逐项取路径）。</param>
/// <param name="Count">计数（集合 / 数组 / 字符串；null → 0）。</param>
/// <param name="NotNull">非空布尔。</param>
/// <param name="AsText">文本形式。</param>
/// <param name="TypeName">运行时类型名。</param>
/// <param name="Message">异常消息。</param>
/// <param name="Thrown">是否为异常对象（期望异常命中）。</param>
/// <param name="Concat">片段按序拼接（null → 空串；条件段见 <see cref="Parts"/>）。</param>
/// <param name="Separator">拼接分隔符（缺省空串）。</param>
/// <param name="Contains">被查集合 / 文本。</param>
/// <param name="Of">被含元素 / 子串 / 字典键。</param>
/// <param name="All">全称（对象形态 = 量词 + <see cref="Call"/>/<see cref="CallTarget"/>/<see cref="AllIn"/>）。</param>
/// <param name="Any">存在（对象形态 = 量词；数组形态 = 析取）。</param>
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
/// <param name="Eq">相等比较（恰两个操作数 <c>[左, 右]</c>）。</param>
/// <param name="Ne">不等比较（恰两个操作数）。</param>
/// <param name="Gt">大于比较（恰两个操作数；数值序）。</param>
/// <param name="Ge">大于等于比较（恰两个操作数）。</param>
/// <param name="Lt">小于比较（恰两个操作数）。</param>
/// <param name="Le">小于等于比较（恰两个操作数）。</param>
/// <param name="AllOf">逻辑合取（任一为假即假，遇 <c>⊥</c> 短路为 <c>⊥</c>）。</param>
/// <param name="AnyOf">逻辑析取（任一为真即真，遇 <c>⊥</c> 短路为 <c>⊥</c>）。</param>
/// <param name="Invoke">调用算子（表达式内调用：执行声明调用并取其结果）。</param>
/// <param name="First">首命中来源（<c>first{over,where,path,orNull}</c>）。</param>
/// <param name="FirstWhere">首命中的保留条件（缺省 = 元素非空）。</param>
/// <param name="FirstPath">首命中项的取值路径（缺省 = 元素本身）。</param>
/// <param name="Agg">聚合来源（遍历该表达式的元素后按 <see cref="AggOp"/> 聚合）。</param>
/// <param name="AggOp">聚合算子：<c>sum</c>（缺省）/ <c>max</c> / <c>min</c> / <c>avg</c>。</param>
/// <param name="AggPath">聚合元素的取值路径（缺省 = 元素本身）。</param>
/// <param name="Filter">筛选来源（按 <see cref="FilterWhere"/> 保留后成集）。</param>
/// <param name="FilterWhere">保留条件（体内 <c>from:row</c> 即当前元素；缺省 = 元素非空）。</param>
/// <param name="FilterPath">保留元素的取值路径（缺省 = 元素本身）。</param>
/// <param name="SameReference">引用同一性比较（恰两个操作数，按 <c>ReferenceEquals</c> 判定）。</param>
/// <param name="Coalesce">空合并（按序取首个非空操作数）。</param>
/// <param name="Dict">键值表装配来源（<c>dict{over,key,value,where,onDuplicate}</c>；空集 = 空字典）。</param>
/// <param name="DictKey">表键表达式（求值文本为空 → 跳过该项，不可作键）。</param>
/// <param name="DictValue">表值表达式（缺省 = 元素本身）。</param>
/// <param name="DictWhere">表项门控（求值为假即跳过该项）。</param>
/// <param name="DictOnDuplicate">键重复处置（缺省 <c>fail</c>；<c>last</c> = 后者覆盖）。</param>
/// <param name="Last">末命中来源（<c>last{over,where,path,orNull}</c>；空集 → null，不抛）。</param>
/// <param name="LastWhere">末命中的保留条件（缺省 = 元素非空）。</param>
/// <param name="LastPath">末命中项的取值路径（缺省 = 元素本身）。</param>
/// <param name="Select">逐元素投影来源（<c>select{over,expr,where}</c>；遍历该表达式的元素后成集）。</param>
/// <param name="SelectExpr">投影表达式（体内 <c>from:row</c> 即当前元素；缺省 = 元素本身）。</param>
/// <param name="SelectWhere">投影门控（求值为假即跳过该元素；缺省 = 全保留）。</param>
/// <param name="FirstOrNull">首命中未命中口径（true = 取 null；缺省 = 未命中即抛）。</param>
/// <param name="LastOrNull">末命中空集口径（true = 取 null）。</param>
/// <param name="Parts">拼接段（条件段形态；见 <see cref="ConcatPartStandard"/>）。</param>
/// <param name="Distinct">去重（保序）。</param>
/// <param name="SequenceEqual">序列相等（恰两个操作数）。</param>
/// <param name="Fold">折叠来源（<c>fold{over,as,init,step}</c>）。</param>
/// <param name="FoldAs">折叠累积值槽名。</param>
/// <param name="FoldInit">折叠初值来源。</param>
/// <param name="FoldStep">折叠累积调用声明（<c>rebind</c> 缺省 true）。</param>
/// <param name="Capture">替身捕获取值算子（见 <see cref="CaptureExprStandard"/>）。</param>
/// <param name="CaptureCount">替身捕获计数算子（见 <see cref="CaptureCountExprStandard"/>）。</param>
/// <param name="FakeCount">替身调用计数算子（见 <see cref="FakeCountExprStandard"/>）。</param>
public sealed record ExprStandard(
    ValueSourceStandard? Source = null,
    string? Path = null,
    ExprStandard? Count = null,
    ExprStandard? NotNull = null,
    ExprStandard? AsText = null,
    ExprStandard? TypeName = null,
    ExprStandard? Message = null,
    ExprStandard? Thrown = null,
    IReadOnlyList<ExprStandard>? Concat = null,
    string? Separator = null,
    ExprStandard? Contains = null,
    ExprStandard? Of = null,
    ExprStandard? All = null,
    ExprStandard? Any = null,
    ExprStandard? AllIn = null,
    string? Call = null,
    string? CallTarget = null,
    ExprStandard? Spread = null,
    IReadOnlyList<string>? Except = null,
    IReadOnlyList<ExprStandard>? CountEqual = null,
    string? Collect = null,
    bool CollectText = false,
    string? SameAsLast = null,
    ExprStandard? IsNull = null,
    ExprStandard? Not = null,
    IReadOnlyList<ExprStandard>? Eq = null,
    IReadOnlyList<ExprStandard>? Ne = null,
    IReadOnlyList<ExprStandard>? Gt = null,
    IReadOnlyList<ExprStandard>? Ge = null,
    IReadOnlyList<ExprStandard>? Lt = null,
    IReadOnlyList<ExprStandard>? Le = null,
    IReadOnlyList<ExprStandard>? AllOf = null,
    IReadOnlyList<ExprStandard>? AnyOf = null,
    InvokeStandard? Invoke = null,
    ExprStandard? First = null,
    ExprStandard? FirstWhere = null,
    string? FirstPath = null,
    ExprStandard? Agg = null,
    string? AggOp = null,
    string? AggPath = null,
    ExprStandard? Filter = null,
    ExprStandard? FilterWhere = null,
    string? FilterPath = null,
    IReadOnlyList<ExprStandard>? SameReference = null,
    IReadOnlyList<ExprStandard>? Coalesce = null,
    ExprStandard? Dict = null,
    ExprStandard? DictKey = null,
    ExprStandard? DictValue = null,
    ExprStandard? DictWhere = null,
    string? DictOnDuplicate = null,
    ExprStandard? Last = null,
    ExprStandard? LastWhere = null,
    string? LastPath = null,
    bool FirstOrNull = false,
    bool LastOrNull = false,
    IReadOnlyList<ConcatPartStandard>? Parts = null,
    ExprStandard? Distinct = null,
    IReadOnlyList<ExprStandard>? SequenceEqual = null,
    ExprStandard? Fold = null,
    string? FoldAs = null,
    ValueSourceStandard? FoldInit = null,
    ChainStandard? FoldStep = null,
    CaptureExprStandard? Capture = null,
    CaptureCountExprStandard? CaptureCount = null,
    FakeCountExprStandard? FakeCount = null,
    ExprStandard? Select = null,
    ExprStandard? SelectExpr = null,
    ExprStandard? SelectWhere = null);

/// <summary>
/// 替身**捕获取值**算子（收敛 3 代数节点）：从依赖替身的调用捕获记录中取某次调用的实参
/// （取代组合族逐 select 私有键 <c>capturedFirst</c>/<c>capturedLast</c>/<c>capturedFirstOrNull</c>/
/// <c>capturedLastOrNull</c>）。
/// <para><see cref="Index"/> 为**节点参数**（实参下标），非顶层键——取值法扩展不再新增顶层键。</para>
/// </summary>
/// <param name="Dep">替身依赖下标。</param>
/// <param name="Member">被捕获的成员名（空 = 全部成员）。</param>
/// <param name="At">取首次（<c>first</c>，缺省）或末次（<c>last</c>）调用。</param>
/// <param name="Index">实参下标（缺省 0 = 首个实参）。</param>
/// <param name="Path">实参上的属性路径（空 = 实参本身）。</param>
/// <param name="OrNull">无捕获 / 零参 / 越界时取 null（缺省 = fail-fast）。</param>
public sealed record CaptureExprStandard(
    int? Dep = null,
    string? Member = null,
    string At = "first",
    int? Index = null,
    string? Path = null,
    bool OrNull = false);

/// <summary>替身**捕获计数**算子（收敛 3 代数节点；取代组合族 <c>capturedCount</c>）。</summary>
/// <param name="Dep">替身依赖下标。</param>
/// <param name="Member">被捕获的成员名（空 = 全部成员）。</param>
public sealed record CaptureCountExprStandard(int? Dep = null, string? Member = null);

/// <summary>替身**调用计数**算子（收敛 3 代数节点；取代组合族 <c>fakeCount</c>）。</summary>
/// <param name="Dep">替身依赖下标。</param>
/// <param name="Member">被计数的成员名。</param>
/// <param name="Key">主键（空 = 不按键聚合）。</param>
public sealed record FakeCountExprStandard(int? Dep = null, string? Member = null, string? Key = null);

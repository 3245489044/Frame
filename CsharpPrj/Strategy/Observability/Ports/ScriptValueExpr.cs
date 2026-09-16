namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

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
    IReadOnlyList<ScriptedExpr>? Coalesce = null);

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


/// <summary>
/// 脚本求值作用域：具名槽表 + 循环变量栈（<c>foreach</c> 内步骤可见当前元素 / 下标 / 元素属性）。
/// <para>槽写入唯一入口 = <see cref="Set"/>；循环变量随迭代入栈出栈（嵌套遍历按最内层优先解析）。</para>
/// </summary>
internal sealed class ScriptedScope
{
    /// <summary>行表 / 词元判定的行帧名（机制内部名；声明面以 <c>from: row</c> 引用当前行）。</summary>
    internal const string RowFrame = "$row";

    private readonly Dictionary<string, object?> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tempDirs = new(StringComparer.Ordinal);
    private readonly List<(string Name, object? Element, int Index)> _loops = new();
    private readonly List<string> _notes = new();

    internal ScriptedScope(string portKey) => PortKey = portKey;

    /// <summary>端口键（诊断定位用）。</summary>
    internal string PortKey { get; }

    /// <summary>登记临时目录（作用域名 → 绝对路径；同名重复即 fail-fast）。</summary>
    internal void SetTempDir(string name, string path)
    {
        if (!_tempDirs.TryAdd(name, path))
            throw new PortBindingViolationException(PortKey, $"端口 '{PortKey}' 的作用域名 '{name}' 重复");
    }

    /// <summary>作用域目录绝对路径（未声明 → fail-fast，不静默给空路径）。</summary>
    internal string TempDir(string? name, string context)
        => name is { Length: > 0 } && _tempDirs.TryGetValue(name, out var path)
            ? path
            : throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未声明的临时目录作用域 '{name}'");

    /// <summary>写入槽（已存在的槽可被后续步骤覆盖：声明序即执行序）。</summary>
    internal void Set(string name, object? value) => _slots[name] = value;

    /// <summary>诊断记录（W1：⊥ 语义导致的「不执行」须留痕，不静默）。</summary>
    internal IReadOnlyList<string> Notes => _notes;

    /// <summary>追加一条诊断记录（执行期不抛，仅供诊断面读取）。</summary>
    internal void Note(string message) => _notes.Add(message);

    /// <summary>读槽（未定义 → fail-fast，不静默给 null）。</summary>
    internal object? Slot(string name, string context)
        => _slots.TryGetValue(name, out var value)
            ? value
            : throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未定义的槽 '{name}'");

    internal bool Has(string name) => _slots.ContainsKey(name);

    /// <summary>收集槽（不存在则建）：循环结果按序追加（G1）。</summary>
    internal List<object?> Collect(string name)
    {
        if (_slots.TryGetValue(name, out var existing) && existing is List<object?> list) return list;

        var created = new List<object?>();
        _slots[name] = created;
        return created;
    }

    /// <summary>入栈循环变量（返回后须配 <see cref="Pop"/>）。</summary>
    internal void Push(string name, object? element, int index) => _loops.Add((name, element, index));

    /// <summary>出栈循环变量。</summary>
    internal void Pop() => _loops.RemoveAt(_loops.Count - 1);

    /// <summary>遍历变量解析：元素本身 / 元素下标 / 元素属性路径（未在遍历中 → fail-fast）。</summary>
    internal object? Item(string? name, int? index, string? path, string context)
    {
        var frame = _loops.LastOrDefault(loop => name is null || string.Equals(loop.Name, name, StringComparison.Ordinal));
        if (frame.Name is null)
            throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未在遍历中的变量 '{name}'");

        var value = index is { } position ? ElementAt(frame.Element, position, context) : frame.Element;
        return path is { Length: > 0 } ? ScriptedValue.PathOf(value, path) : value;
    }

    /// <summary>元素下标取值（列表 / 数组 / 字符串；越界 / 非序载体 → fail-fast）。</summary>
    private object? ElementAt(object? element, int position, string context)
    {
        switch (element)
        {
            case null:
                return null;
            case System.Collections.IList list when position >= 0 && position < list.Count:
                return list[position];
            case string text when position >= 0 && position < text.Length:
                return text[position].ToString();
            default:
                throw new PortBindingViolationException(
                    PortKey,
                    $"端口 '{PortKey}' 的 {context} 对 {element?.GetType().Name ?? "null"} 取下标 {position} 越界");
        }
    }
}

/// <summary>
/// 脚本取值单源（<c>bind</c> / 步骤实参 / 表达式源共用）：来源解析 + 目标类型转换 + fail-fast 诊断。
/// <para>分工：脚本家族的来源词汇含遍历变量与槽引用（组合绑定的三种来源不含），故与
/// <see cref="ComposedArgs"/> 并列而不同源；值变换（路径 / 计数 / JSON 归一）单源 = <see cref="PortValues"/>。</para>
/// </summary>
internal static class ScriptedValue
{
    /// <summary>按来源取值（不做类型转换；缺来源 / 未定义槽 / 越界依赖 → fail-fast）。</summary>
    internal static object? Resolve(
        ScriptedArgSpec? spec, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        if (spec is null)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 未声明取值来源（from）");

        switch (spec.From)
        {
            case "arg":
                return Positional(spec, input, scope.PortKey, context);

            case "with":
                return With(spec, input, scope.PortKey, context);

            case "item":
                return scope.Item(spec.Key, spec.Index, spec.Path, context);

            case "row":
                return scope.Item(ScriptedScope.RowFrame, spec.Index, spec.Path, context);

            case "table":
                return Table(spec, scope, input, deps, context);

            case "presence":
                return Presence(spec, scope, input, deps, context);

            case "tmpDir":
                return scope.TempDir(spec.Scope, context);

            case "tmpPath":
                return TempFile(spec, scope, context);

            case "var":
                if (string.IsNullOrWhiteSpace(spec.Key))
                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 var 来源缺 key（槽名）");

                var slot = scope.Slot(spec.Key!, context);
                return spec.Path is { Length: > 0 } path ? PortValues.PathOf(slot, path) : slot;

            case "dep":
                return spec.Dep is { } index
                    ? ComposedDeps.Dep(deps, index, index, scope.PortKey)
                    : throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 dep 来源缺 dep（依赖下标）");

            case "literal":
                return spec.Value is { } literal ? PortValues.Unwrap(literal) : null;

            case "member":
                return StaticMember(spec, scope.PortKey, context);

            case null or "" when spec.Construct is not null:
                return ScriptedConstruction.Build(spec.Construct!, scope, input, deps, context);

            case "new":
                return spec.Construct is { } construct
                    ? ScriptedConstruction.Build(construct, scope, input, deps, context)
                    : throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 new 来源缺 construct（构造声明）");

            case null or "" when spec.Optional:
                return null;

            default:
                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 {context} 取值来源 '{spec.From}' 未知" +
                    "（arg|with|item|row|var|dep|literal|member|new|table|presence|tmpPath|tmpDir）");
        }
    }

    /// <summary>按目标类型取值（声明 <c>type</c> 优先，否则用调用方给的形参类型；无声明 → 目标类型默认值）。</summary>
    internal static object? ResolveTyped(
        ScriptedArgSpec? spec, Type target, ScriptedScope scope, IPortInput input, object?[] deps, string context)
        => spec is null
            ? ReflectivePortResolver.DefaultFor(target)
            : Convert(
                Resolve(spec, scope, input, deps, context),
                spec,
                DeclaredType(spec) ?? target,
                scope.PortKey,
                context);

    /// <summary>声明目标类型（<c>type</c> + <c>genericArgs</c>；未声明 → null）。</summary>
    internal static Type? DeclaredType(ScriptedArgSpec spec)
        => spec.Type is { Length: > 0 } name ? ComposedDeps.ResolveType(name, spec.GenericArgs) : null;

    /// <summary>属性路径取值（单源转调 <see cref="PortValues.PathOf"/>）。</summary>
    internal static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>作用域内文件绝对路径（<c>name</c> 不得含路径分隔符：子目录由 <c>dir</c> 表达式声明）。</summary>
    private static string TempFile(ScriptedArgSpec spec, ScriptedScope scope, string context)
    {
        if (spec.Name is not { Length: > 0 } name)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 tmpPath 来源缺 name（文件名）");

        if (name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的文件名 '{name}' 不得含路径分隔符（子目录用 dir 声明）");

        return System.IO.Path.Combine(scope.TempDir(spec.Scope, context), name);
    }

    /// <summary>
    /// 行表装配（P3）：行集 → 键值表（空首列 / 重复键的处置与 L0 <c>SpecTables.Rows</c> 同口径）。
    /// <para>键 = 首列（<c>separator</c> 非空时按分隔符拼接全部列）；值 = <c>rowValue</c> 表达式
    /// （体内 <c>from:row</c> 取列），缺省 = 首列文本。逐行以行帧入栈（行内可再引 <c>item</c> / <c>var</c>）。</para>
    /// </summary>
    private static Dictionary<string, object?> Table(
        ScriptedArgSpec spec, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        var source = spec.Rows ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 table 来源缺 rows（行集来源）");

        var rows = PortValues.Elements(Resolve(source, scope, input, deps, $"{context} 的行集")).ToArray();
        var separator = spec.Separator ?? "|";
        var onBlank = spec.OnBlankFirstKey ?? "fail";
        var onDuplicate = spec.OnDuplicate ?? "fail";
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < rows.Length; index++)
        {
            scope.Push(ScriptedScope.RowFrame, rows[index], index);
            try
            {
                if (spec.Filter is { } filter
                    && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(
                        filter, scope, input, deps, $"{context} 的行过滤")))
                {
                    continue;
                }

                var cells = PortValues.Elements(rows[index]).ToArray();
                var head = cells.Length > 0 ? PortValues.Text(cells[0]) : null;
                if (string.IsNullOrWhiteSpace(head))
                {
                    if (string.Equals(onBlank, "skip", StringComparison.Ordinal)) continue;

                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 行首列为空（第 {index + 1} 行）");
                }

                var key = separator.Length == 0
                    ? head
                    : string.Join(separator,
                        cells.Select(cell => PortValues.Text(cell))
                             .Where(cell => !string.IsNullOrEmpty(cell)));

                var value = spec.RowValue is { } rowValue
                    ? ScriptedExprEvaluator.Evaluate(rowValue, scope, input, deps, context)
                    : head;

                if (map.TryAdd(key, value)) continue;
                if (string.Equals(onDuplicate, "last", StringComparison.Ordinal))
                {
                    map[key] = value;
                    continue;
                }

                throw new PortBindingViolationException(
                    scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的键 '{key}' 重复（第 {index + 1} 行）");
            }
            finally
            {
                scope.Pop();
            }
        }

        return map;
    }

    /// <summary>
    /// 存在性判定表（P3）：词元集 → 逐项布尔（<c>filter</c> 体内 <c>from:row</c> 即当前词元；
    /// 缺 <c>filter</c> 时按「词元非空」判定）。缺首列为空的词元直接跳过（不可作键）。
    /// </summary>
    private static Dictionary<string, object?> Presence(
        ScriptedArgSpec spec, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        var source = spec.Items ?? spec.Rows ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 presence 来源缺 items（词元集来源）");

        var tokens = PortValues.Elements(Resolve(source, scope, input, deps, $"{context} 的词元集")).ToArray();
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < tokens.Length; index++)
        {
            if (PortValues.Text(tokens[index]) is not { Length: > 0 } token) continue;
            if (spec.Distinct && map.ContainsKey(token)) continue;

            scope.Push(ScriptedScope.RowFrame, tokens[index], index);
            try
            {
                map[token] = spec.Filter is { } filter
                    ? PortValues.Truthy(ScriptedExprEvaluator.Evaluate(filter, scope, input, deps, $"{context} 的判定"))
                    : tokens[index] is not null;
            }
            finally
            {
                scope.Pop();
            }
        }

        return map;
    }

    /// <summary>按 <c>convert</c> 与目标形参类型转换（无 <c>convert</c>：JSON 反序列化 / 原类型直投）。</summary>
    private static object? Convert(object? raw, ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        if (raw is null)
        {
            if (spec.Default is { } fallback) return PortValues.Coerce(fallback, target);
            if (spec.Optional) return ReflectivePortResolver.DefaultFor(target);
            return null;
        }

        switch (spec.Convert)
        {
            case "string":
                return PortValues.Text(raw);

            case "csv":
                return Csv(PortValues.Text(raw) ?? string.Empty, target, portKey, context);

            case "array":
                return ArrayLike(raw, spec, target, portKey, context);

            case "graph":
                return raw is JsonElement graph ? PortValues.ToGraph(graph) : raw;

            case "json":
                return PortValues.Coerce(
                    raw is JsonElement element
                        ? element
                        : JsonDocument.Parse(PortValues.Text(raw) ?? "null").RootElement,
                    target);

            case "bool":
                return bool.TryParse(PortValues.Text(raw), out var flag)
                    ? flag
                    : throw Mismatch(portKey, context, raw, "布尔");

            case "int":
                return int.TryParse(PortValues.Text(raw), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw Mismatch(portKey, context, raw, "整数");

            case "double":
                return double.TryParse(PortValues.Text(raw), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var real)
                    ? real
                    : throw Mismatch(portKey, context, raw, "数值");

            case "object" or "passthrough":
                return PortValues.FitsDirect(raw, target)
                    ? raw
                    : throw Mismatch(portKey, context, raw, target.Name);

            case null or "":
                return Direct(raw, target, portKey, context);

            default:
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context} 的 convert '{spec.Convert}' 未知" +
                    "（string|bool|int|double|csv|json|array|object|passthrough）");
        }
    }

    private static PortBindingViolationException Mismatch(string portKey, string context, object raw, string expected)
        => new(portKey, $"端口 '{portKey}' 的 {context} 的值 '{PortValues.Text(raw)}' 不能转换为 {expected}");

    /// <summary>无转换声明：JSON 值按目标类型反序列化；原类型直投；值类型按文本宽松解析。</summary>
    private static object? Direct(object raw, Type target, string portKey, string context)
    {
        if (raw is JsonElement) return PortValues.Coerce(raw, target);
        if (target == typeof(object) || target.IsInstanceOfType(raw)) return raw;

        if (target.IsValueType && raw is string text)
        {
            try
            {
                return System.Convert.ChangeType(text, target, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                throw Mismatch(portKey, context, raw, target.Name);
            }
        }

        throw Mismatch(portKey, context, raw, target.Name);
    }

    /// <summary>CSV 文本 → 目标集合类型（逗号拆分；数组 / 字符串集合均可）。</summary>
    private static object? Csv(string text, Type target, string portKey, string context)
    {
        var parts = text.Length == 0
            ? new List<string?>()
            : text.Split(',').Select(item => (string?)item.Trim()).ToList();

        if (target.IsArray) return parts.ToArray();
        if (target.IsAssignableFrom(parts.GetType())) return parts;
        if (target == typeof(object)) return parts;

        throw Mismatch(portKey, context, text, target.Name);
    }

    /// <summary>
    /// 位置实参取值（越界 → fail-fast）：对象直投形态（<see cref="PortValues.IsDirectPositional"/>）原样保留原对象
    /// （不 <c>ToString</c> 文本化），其余按 <c>convert</c> 原义取文本。
    /// </summary>
    private static object? ArrayLike(
        object? raw, ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        var element = ElementTypeOf(spec, target, portKey, context);
        var items = PortValues.Elements(raw).ToArray();
        var array = Array.CreateInstance(element, items.Length);

        for (var i = 0; i < items.Length; i++)
            array.SetValue(ConvertElement(items[i], element, portKey, context), i);

        if (target.IsInstanceOfType(array)) return array;

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(target)!;
            foreach (var item in array) list.Add(item);
            return list;
        }

        return array;
    }

    private static Type ElementTypeOf(ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        if (spec.ElementType is { Length: > 0 } name) return ComposedDeps.ResolveType(name, null);
        if (target.IsArray && target.GetElementType() is { } element) return element;

        if (target.IsGenericType
            && target.GetGenericArguments() is { Length: 1 } arguments
            && target != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(target))
        {
            return arguments[0];
        }

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context} 的 array 转换需要集合 / 数组目标类型或 elementType 声明（当前 {target.Name}）");
    }

    private static object? ConvertElement(object? value, Type element, string portKey, string context)
    {
        if (value is null) return null;
        if (element == typeof(object) || element.IsInstanceOfType(value)) return value;

        if (value is JsonElement json) return PortValues.Coerce(json, element);

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context} 的元素 {value.GetType().Name} 不能转为 {element.Name}");
    }

    private static object? StaticMember(ScriptedArgSpec spec, string portKey, string context)
    {
        if (spec.Type is not { Length: > 0 } typeName)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 member 来源缺 type（声明类型全名）");

        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 member 来源缺 key（成员名）");

        var member = ReflectivePortResolver.ResolveMember(
            ComposedDeps.ResolveType(typeName, spec.GenericArgs), portKey, spec.Key!, isStatic: true);

        return member switch
        {
            PropertyInfo property => property.GetValue(null),
            FieldInfo field => field.GetValue(null),
            _ => null,
        };
    }

    private static object? Positional(ScriptedArgSpec spec, IPortInput input, string portKey, string context)
    {
        var position = spec.Index
            ?? throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 arg 来源缺 index（位置实参下标）");

        if (position >= input.Args.Count)
        {
            if (spec.Optional) return null;

            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 需要第 {position} 个位置实参，实际只有 {input.Args.Count} 个");
        }

        var raw = input.Args[position];
        if (spec.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(spec.Convert, raw)) return raw;

        return raw as string ?? raw?.ToString() ?? string.Empty;
    }

    /// <summary>具名实参取值（原始 JSON；缺键 → <c>optional</c> 决定 null 或 fail-fast）。</summary>
    private static object? With(ScriptedArgSpec spec, IPortInput input, string portKey, string context)
    {
        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 with 来源缺 key（具名实参键）");

        if (input.With.TryGetValue(spec.Key!, out var value)) return value;
        if (spec.Optional) return null;

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 {context} 缺具名实参 '{spec.Key}'（with）");
    }
}

/// <summary>
/// 投影表达式求值器（E22 自证对象）：源值 → 算子递归求值；未知形态 / 悬空引用 → fail-fast。
/// <para>算子语义（规范 §5）：<c>count</c> 计数、<c>notNull</c> 非空、<c>text</c> 文本、<c>typeName</c>/<c>message</c>/<c>thrown</c>
/// 异常三态、<c>concat</c> 拼接、<c>contains</c> 包含、<c>all</c>/<c>any</c> 量词、<c>spread</c> 动态键展开、
/// <c>countEqual</c> 取等、<c>collect</c> 收集槽逐项投影。</para>
/// </summary>
internal static class ScriptedExprEvaluator
{
    /// <summary>求值（<paramref name="field"/> 仅用于诊断定位）。</summary>
    internal static object? Evaluate(
        ScriptedExpr expr, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var context = $"投影字段 '{field}'";

        if (expr.Source is { } source) return ScriptedValue.Resolve(source, scope, input, deps, context);

        if (expr.Count is { } count) return Count(Sub(count, scope, input, deps, field), scope.PortKey, field);
        if (expr.NotNull is { } notNull) return Sub(notNull, scope, input, deps, field) is not null;
        if (expr.AsText is { } asText) return PortValues.Text(Sub(asText, scope, input, deps, field));
        if (expr.TypeName is { } typeName) return TypeNameOf(Sub(typeName, scope, input, deps, field));
        if (expr.Message is { } message) return (Sub(message, scope, input, deps, field) as Exception)?.Message;
        if (expr.Thrown is { } thrown) return Sub(thrown, scope, input, deps, field) is Exception;
        if (expr.Concat is { } concat) return Concat(expr, concat, scope, input, deps, field);
        if (expr.Contains is { } contains) return Contains(expr, contains, scope, input, deps, field);
        if (expr.All is { } all) return Quantify(expr, all, scope, input, deps, field, every: true);
        if (expr.Any is { } any) return Quantify(expr, any, scope, input, deps, field, every: false);
        if (expr.Spread is { } spread) return Spread(expr, spread, scope, input, deps, field);
        if (expr.CountEqual is { Count: 2 } equal) return CountEqual(equal, scope, input, deps, field);
        if (expr.IsNull is { } isNull) return Sub(isNull, scope, input, deps, field) is null;
        if (expr.Not is { } not) return Not(Sub(not, scope, input, deps, field));
        if (expr.AllOf is { } allOf) return Junction(allOf, scope, input, deps, field, every: true);
        if (expr.AnyOf is { } anyOf) return Junction(anyOf, scope, input, deps, field, every: false);
        if (expr.Eq is { } eq) return Compare(eq, scope, input, deps, field, "eq");
        if (expr.Ne is { } ne) return Compare(ne, scope, input, deps, field, "ne");
        if (expr.Gt is { } gt) return Compare(gt, scope, input, deps, field, "gt");
        if (expr.Ge is { } ge) return Compare(ge, scope, input, deps, field, "ge");
        if (expr.Lt is { } lt) return Compare(lt, scope, input, deps, field, "lt");
        if (expr.Le is { } le) return Compare(le, scope, input, deps, field, "le");
        if (expr.Invoke is { } call)
            return ScriptedInvokeRunner.Run(scope.PortKey, call, specs: null, scope, input, deps).Result;

        if (expr.First is { } first) return First(expr, first, scope, input, deps, field);
        if (expr.Agg is { } agg) return Aggregate(expr, agg, scope, input, deps, field);
        if (expr.Filter is { } filter) return Filter(expr, filter, scope, input, deps, field);
        if (expr.SameReference is { Count: 2 } same) return SameReference(same, scope, input, deps, field);
        if (expr.Coalesce is { Count: 2 } coalesce) return Coalesce(coalesce, scope, input, deps, field);

        if (expr.Collect is { Length: > 0 } collect) return Collect(expr, collect, scope, input, deps, field);

        throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的表达式未声明来源或算子");
    }

    private static object? Sub(
        ScriptedExpr expr, ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => Evaluate(expr, scope, input, deps, field);

    private static int Count(object? value, string portKey, string field)
        => PortValues.TryCount(value)
           ?? throw new PortBindingViolationException(
               portKey, $"端口 '{portKey}' 的投影字段 '{field}' 的 count 不适用于 {value!.GetType().Name}");

    private static string? TypeNameOf(object? value) => value switch
    {
        Exception exception => exception.GetType().Name,
        Type type => type.Name,
        null => null,
        _ => value.GetType().Name,
    };

    private static string Concat(
        ScriptedExpr expr, IReadOnlyList<ScriptedExpr> parts,
        ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => string.Join(expr.Separator ?? string.Empty,
            parts.Select(part => PortValues.Text(Sub(part, scope, input, deps, field)) ?? string.Empty));

    private static bool Contains(
        ScriptedExpr expr, ScriptedExpr container, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var needle = expr.Of is { } of
            ? Sub(of, scope, input, deps, field)
            : throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 contains 缺少 of（被含元素）");

        var value = Sub(container, scope, input, deps, field);

        return value switch
        {
            null => false,
            string text => text.Contains(PortValues.Text(needle) ?? string.Empty, StringComparison.Ordinal),
            System.Collections.IDictionary map => map.Contains(needle),
            _ => PortValues.Elements(value).Any(item => Equal(item, needle)),
        };
    }

    private static bool Quantify(
        ScriptedExpr expr, ScriptedExpr over, ScriptedScope scope, IPortInput input, object?[] deps,
        string field, bool every)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        if (items.Length == 0) return every;

        var predicate = Predicate(expr, scope);
        var bag = expr.AllIn is { } allIn
            ? PortValues.Elements(Sub(allIn, scope, input, deps, field)).ToArray()
            : null;

        bool Match(object? item)
            => predicate is not null ? predicate(item)
               : bag is not null ? bag.Any(candidate => Equal(candidate, item))
               : item is not null;

        return every ? items.All(Match) : items.Any(Match);
    }

    /// <summary>量词判据：声明 <c>call</c> 时取静态谓词（每元素求布尔）；否则按 <c>allIn</c> / 非空判据处理。</summary>
    private static Func<object?, bool>? Predicate(ScriptedExpr expr, ScriptedScope scope)
    {
        if (expr.Call is not { Length: > 0 } name) return null;

        var target = expr.CallTarget is { Length: > 0 } typeName
            ? ComposedDeps.ResolveType(typeName, null)
            : throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的谓词 '{name}' 缺 callTarget（静态类型全名）");

        var method = ReflectivePortResolver.ResolveMethod(target, scope.PortKey, name, 1, isStatic: true);
        return item => PortInvocation.Execute(() => method.Invoke(null, new[] { item }), expect: null) is true;
    }

    private static IReadOnlyDictionary<string, object?> Spread(
        ScriptedExpr expr, ScriptedExpr source, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var entries = PortValues.Spread(Sub(source, scope, input, deps, field));
        if (expr.Except is not { Count: > 0 } except) return entries;

        var kept = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
            if (!except.Contains(key, StringComparer.Ordinal)) kept[key] = value;

        return kept;
    }

    private static bool CountEqual(
        IReadOnlyList<ScriptedExpr> operands, ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => Count(Sub(operands[0], scope, input, deps, field), scope.PortKey, field)
           == Count(Sub(operands[1], scope, input, deps, field), scope.PortKey, field);

    /// <summary>逻辑非（三值：<c>⊥</c>（null）→ <c>⊥</c>）。</summary>
    private static object? Not(object? value) => value is null ? null : !PortValues.Truthy(value);

    /// <summary>
    /// 逻辑合取 / 析取（三值：合取遇假即假、析取遇真即真；否则遇 <c>⊥</c> → <c>⊥</c>）。
    /// <para><c>⊥</c> 语义：调用方（步骤 <c>when</c>）按「不执行」处理（<c>Truthy(null)=false</c>），不静默成真。</para>
    /// </summary>
    private static object? Junction(
        IReadOnlyList<ScriptedExpr> parts, ScriptedScope scope, IPortInput input, object?[] deps,
        string field, bool every)
    {
        if (parts.Count == 0)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 {(every ? "all" : "any")} 算子缺子表达式（不可空表）");

        var bottom = false;
        foreach (var part in parts)
        {
            var value = Sub(part, scope, input, deps, field);
            if (value is null)
            {
                bottom = true;
                continue;
            }

            if (PortValues.Truthy(value) != every) return !every;
        }

        return bottom ? null : (object)every;
    }

    /// <summary>
    /// 二元比较（三值：任一侧缺值 → <c>⊥</c>；<c>eq</c>/<c>ne</c> 按值相等，其余按数值序优先、回落文本序）。
    /// </summary>
    private static object? Compare(
        IReadOnlyList<ScriptedExpr> operands, ScriptedScope scope, IPortInput input, object?[] deps,
        string field, string op)
    {
        if (operands.Count != 2)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 {op} 算子须恰两个操作数（当前 {operands.Count} 个）");

        var left = Sub(operands[0], scope, input, deps, field);
        var right = Sub(operands[1], scope, input, deps, field);

        if (left is null || right is null) return null;

        if (op is "eq" or "ne")
        {
            var same = Equal(left, right);
            return op == "eq" ? same : !same;
        }

        var order = PortValues.Number(left) is { } leftNumber && PortValues.Number(right) is { } rightNumber
            ? leftNumber.CompareTo(rightNumber)
            : string.CompareOrdinal(PortValues.Text(left), PortValues.Text(right));

        return op switch
        {
            "gt" => order > 0,
            "ge" => order >= 0,
            "lt" => order < 0,
            "le" => order <= 0,
            _ => throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的比较算子 '{op}' 未知（eq|ne|gt|ge|lt|le）"),
        };
    }

    private static object? First(
        ScriptedExpr expr, ScriptedExpr over, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var index = -1;

        foreach (var item in items)
        {
            index++;
            scope.Push(ScriptedScope.RowFrame, item, index);

            bool hit;
            try
            {
                hit = expr.FirstWhere is { } predicate
                    ? PortValues.Truthy(Sub(predicate, scope, input, deps, field))
                    : item is not null;
            }
            finally
            {
                scope.Pop();
            }

            if (!hit) continue;

            return expr.FirstPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item;
        }

        throw new PortBindingViolationException(
            scope.PortKey,
            $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 first 未命中（{items.Length} 个元素无匹配）");
    }

    /// <summary>
    /// 聚合取值（W3）：对来源元素逐项取 <see cref="ScriptedExpr.AggPath"/> → 数值化 → 按 <c>sum|max|min|avg</c> 聚合。
    /// <para>口径：非数值元素即抛（不静默跳过）；<c>sum</c> 空集 = 0，<c>max|min|avg</c> 空集即抛；
    /// 全整数输入且结果可容纳于 <see cref="int"/> 时按整数返回（与夹具的整数期望可比）。</para>
    /// </summary>
    private static object? Aggregate(
        ScriptedExpr expr, ScriptedExpr over, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var op = expr.AggOp ?? "sum";
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var numbers = new List<double>(items.Length);

        foreach (var item in items)
        {
            var value = expr.AggPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item;
            if (value is null) continue;

            if (PortValues.Number(value) is not { } number)
                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg 元素 '{PortValues.Text(value)}' 非数值");

            numbers.Add(number);
        }

        var integral = numbers.All(number => Math.Abs(number % 1) < double.Epsilon);
        var total = numbers.Sum();

        return op switch
        {
            "sum" => integral && total is >= int.MinValue and <= int.MaxValue ? (int)total : (object)total,
            "max" when numbers.Count > 0 => Extreme(numbers.Max(), integral),
            "min" when numbers.Count > 0 => Extreme(numbers.Min(), integral),
            "avg" when numbers.Count > 0 => numbers.Average(),
            "max" or "min" or "avg" => throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg.{op} 无可取值元素"),
            _ => throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg 算子 '{op}' 未知（sum|max|min|avg）"),
        };
    }

    /// <summary>聚合取值结果归一：整数输入按 <see cref="int"/> 返回（可比性），否则原样。</summary>
    private static object Extreme(double value, bool integral)
        => integral && value is >= int.MinValue and <= int.MaxValue ? (int)value : value;

    /// <summary>
    /// 筛选成集（W8）：遍历来源元素 → 按 <see cref="ScriptedExpr.FilterWhere"/> 保留（真值判定；<c>⊥</c> 视同不保留）
    /// → 逐项取 <see cref="ScriptedExpr.FilterPath"/> → 数组。空集 = 空数组（不抛）。
    /// </summary>
    private static object?[] Filter(
        ScriptedExpr expr, ScriptedExpr over, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var kept = new List<object?>(items.Length);
        var index = -1;

        foreach (var item in items)
        {
            index++;
            scope.Push(ScriptedScope.RowFrame, item, index);

            bool hit;
            try
            {
                hit = expr.FilterWhere is { } predicate
                    ? PortValues.Truthy(Sub(predicate, scope, input, deps, field))
                    : item is not null;
            }
            finally
            {
                scope.Pop();
            }

            if (!hit) continue;
            kept.Add(expr.FilterPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item);
        }

        return kept.ToArray();
    }

    /// <summary>引用同一性（W5）：<c>ReferenceEquals(左, 右)</c>（值相等但非同一引用 → false）。</summary>
    private static bool SameReference(
        IReadOnlyList<ScriptedExpr> operands, ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => ReferenceEquals(
            Sub(operands[0], scope, input, deps, field),
            Sub(operands[1], scope, input, deps, field));

    private static object? Coalesce(
        IReadOnlyList<ScriptedExpr> operands, ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => Sub(operands[0], scope, input, deps, field)
           ?? Sub(operands[1], scope, input, deps, field);

    private static object?[] Collect(
        ScriptedExpr expr, string name, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var items = PortValues.Elements(scope.Slot(name, $"投影字段 '{field}' 的 collect")).ToArray();

        var projected = items
            .Select(item => expr.Path is { Length: > 0 } path ? PortValues.PathOf(item, path) : item)
            .ToArray();

        if (expr.SameAsLast is { Length: > 0 } other)
        {
            var last = PortValues.Elements(
                scope.Slot(other, $"投影字段 '{field}' 的 sameAsLast")).LastOrDefault();

            return projected.Select(item => (object?)ReferenceEquals(item, last)).ToArray();
        }

        return expr.CollectText
            ? projected.Select(item => (object?)PortValues.Text(item)).ToArray()
            : projected;
    }

    /// <summary>值等价（JSON 元素按原义归一；数值跨 CLR 类型比较；字符串按序号比较）。</summary>
    private static bool Equal(object? left, object? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a is string text && b is string other)
            return string.Equals(text, other, StringComparison.Ordinal);

        if (a is not string && b is not string
            && PortValues.Number(a) is { } leftNumber && PortValues.Number(b) is { } rightNumber)
            return leftNumber == rightNumber;

        return Equals(a, b);
    }

    /// <summary>JSON 标量 → CLR 原义值（结构保留元素，供路径 / 计数 / 展开使用）。</summary>
    private static object? Normalize(object? value)
        => value is JsonElement element && element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
            ? PortValues.Unwrap(element)
            : value;
}



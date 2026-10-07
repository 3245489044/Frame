using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// **端口执行单源**（J2 通用框架 · 终态）：承载「脚本签名面」的完整求值序 ——
/// 场景分派 → 输入绑定 → 作用域 → 依赖构建 → 步骤序列（指令） → 投影 → 载体；
/// 亦以**平坦程序**模式执行 reflective / composed 规格（指令循环 + 产物）。
/// <para>
/// 消费方：① 求值引擎（<see cref="PortEvaluator"/> 持有规格）；② 兼容 facade（<c>ScriptedBinding</c>，③ 声明入口）。
/// 两方共用本件 ⇒ 语义不可能分叉（终态 = 单一执行实现）。
/// </para>
/// <para>零领域词汇（类型 / 方法 / 字段名一律不透明字符串）；依赖构建与调用折叠复用单源
/// （<see cref="ComposedDeps"/> / <see cref="PortInvocation"/> / <see cref="ScriptedSteps"/>）。</para>
/// </summary>
internal sealed class PortExecutor
{
    private readonly string _portKey;
    private readonly IReadOnlyDictionary<string, ValueSourceStandard> _binds;
    private readonly IReadOnlyList<DepStandard> _deps;
    private readonly IReadOnlyList<FieldExprStandard> _project;
    private readonly DeliverStandard _deliver;
    private readonly IReadOnlyList<ScopeStandard> _scopes;
    private readonly ScenarioStandard? _scenarios;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private static readonly object?[] NoDeps = Array.Empty<object?>();

    /// <summary>脚本步骤指令序列（③ 声明经 <see cref="PortSpecCompiler.Steps"/> 编译；执行单源 = <see cref="ScriptedSteps"/>）。</summary>
    private readonly IReadOnlyList<PortInstr> _program;

    /// <summary>步骤执行宿主（端口键 + 依赖声明 + 留痕回传；执行单源 = <see cref="ScriptedSteps"/>）。</summary>
    private readonly ScriptedSteps.Host _host;

    private static readonly IReadOnlyDictionary<string, ValueSourceStandard> EmptyBinds =
        new Dictionary<string, ValueSourceStandard>(StringComparer.Ordinal);

    /// <summary>声明的收集槽名集（步骤 / 遍历 / 投影 collect / sameAsLast）。</summary>
    private readonly IReadOnlySet<string> _collections;

    /// <summary>场景体绑定（构造期预构建：工厂表随宿主传播；分派不再逐次重建与重校验）。</summary>
    private readonly IReadOnlyDictionary<string, PortExecutor>? _scenarioCases;

    /// <summary>按 ③ 绑定声明构造由兼容 facade <c>ScriptedBinding</c> 承担（本件只认 ① 记录与 ② 规格）。</summary>

    /// <summary>按 ① 标准声明构造（facade 入口：只认标准记录 + 工厂表，不识别 ③ 绑定声明家族）。</summary>
    internal PortExecutor(
        string portKey, PortImplStandard impl,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories = null)
        : this(
            portKey,
            PortGuard.Required(impl, nameof(impl)).Binds ?? EmptyBinds,
            impl.Deps ?? Array.Empty<DepStandard>(),
            PortSpecCompiler.Steps(impl.Script ?? Array.Empty<StepStandard>()),
            impl.ScriptProject ?? Array.Empty<FieldExprStandard>(),
            impl.Deliver ?? new DeliverStandard(),
            impl.Scopes ?? Array.Empty<ScopeStandard>(),
            impl.Scenarios,
            factories)
    {
    }

    /// <summary>按 ② 规格构造（求值引擎入口）：签名面 = ① 记录引用（binds / deps / 投影 / 载体 / 作用域 / 场景）+ 指令序列。</summary>
    internal PortExecutor(PortSpec spec, PortInjection injection)
        : this(
            PortGuard.Required(spec, nameof(spec)).PortKey,
            spec.Binds ?? EmptyBinds,
            spec.Deps ?? Array.Empty<DepStandard>(),
            spec.Program,
            spec.ScriptProject ?? Array.Empty<FieldExprStandard>(),
            spec.Deliver ?? new DeliverStandard(),
            spec.Scopes ?? Array.Empty<ScopeStandard>(),
            spec.Scenarios,
            injection.Factories)
    {
    }

    /// <summary>
    /// 主构造：签名面 + 指令序列 + 工厂表（构造期校验：无行为声明 / 载体冲突 / 引用形态残缺 → 即抛，防静默绑错）。
    /// </summary>
    private PortExecutor(
        string portKey,
        IReadOnlyDictionary<string, ValueSourceStandard> binds,
        IReadOnlyList<DepStandard> deps,
        IReadOnlyList<PortInstr> program,
        IReadOnlyList<FieldExprStandard> project,
        DeliverStandard deliver,
        IReadOnlyList<ScopeStandard> scopes,
        ScenarioStandard? scenarios,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);

        _portKey = portKey;
        _binds = binds;
        _deps = deps;
        _project = project;
        _deliver = deliver;
        _scopes = scopes;
        _scenarios = scenarios;
        _factories = factories ?? PortInjection.Empty.Factories;
        _program = program;
        _host = new ScriptedSteps.Host(portKey, _deps, notes => _lastNotes.Value = notes);
        _collections = ScriptedSteps.Collections(_program, _project);

        if (_program.Count == 0 && _project.Count == 0 && _deliver.Capture is null
            && _deliver.DeferSteps is not { Count: > 0 } && _deliver.Value is null
            && _scenarios is null)
        {
            throw new PortBindingViolationException(portKey, PortTerminalReasons.ScriptedNoBehavior(portKey));
        }

        if (_deliver.Defer && _deliver.Issues)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的载体 defer 与 issues 互斥（二选一）");

        ValidateKind();
        ValidateScopes();
        ValidateScenarios();
        _scenarioCases = BuildScenarios();
    }

    /// <summary>
    /// 最近一次求值的诊断记录（W1：步骤 <c>when</c> / 遍历 <c>where</c> 求值为 ⊥ ⇒ 不执行且留痕，不静默）。
    /// 绑定实例在端口目录中为单例、可被并发调用 —— 记录按异步执行流（AsyncLocal）隔离，防调用间最后写胜串台。
    /// </summary>
    private readonly AsyncLocal<IReadOnlyList<string>?> _lastNotes = new();

    /// <summary>最近一次求值的诊断记录（按当前异步执行流隔离；尚未执行过为空清单）。</summary>
    public IReadOnlyList<string> LastNotes => _lastNotes.Value ?? Array.Empty<string>();

    /// <summary>求值入口（本件唯一执行序；调用方 = 求值引擎 / 兼容 facade）。</summary>
    internal object? Run(IPortInput input)
    {
        PortGuard.Required(input, nameof(input));

        if (_scenarioCases is not null) return Dispatch(input);

        var deliverKind = KindOf(_deliver);

        if (deliverKind == "action")
            return new DeferredAction(() => _deliver.DeferSteps is { Count: > 0 } steps
                ? RunDeferred(steps, input)
                : Execute(input));

        if (deliverKind == "issues" && _deliver.Capture is { } capture)
            return new PortIssues(CaptureIssues(capture, input));

        return Execute(input);
    }

    /// <summary>
    /// 多场景探针分派（W2：<c>PortProbeRouter.Declare</c> 的一等声明等价）：取场景名 → 命中即执行该场景主体
    /// （独立绑定实例，共享同一端口键与工厂表）；**未命中即抛**，诊断列全部可用场景名（不静默回落）。
    /// </summary>
    private object? Dispatch(IPortInput input)
    {
        var scenarios = _scenarios!;
        var probe = NewScope();
        var chosen = PortValues.Text(ScriptedValue.Resolve(
            scenarios.Select with { Optional = true }, probe, input, NoDeps, "场景选择（select）"));

        if (chosen is not { Length: > 0 } || !_scenarioCases!.TryGetValue(chosen, out var body))
        {
            throw new PortBindingViolationException(
                _portKey,
                $"端口 '{_portKey}' 的探针 '{chosen ?? "<空>"}' 未命中" +
                $"（可用场景：{string.Join(", ", _scenarioCases!.Keys.OrderBy(name => name, StringComparer.Ordinal))}）");
        }

        return body.Run(input);
    }

    /// <summary>场景体绑定预构建（工厂表随宿主传播：场景内 <c>deps.factory</c> 不再依赖调用方补注入）。</summary>
    private IReadOnlyDictionary<string, PortExecutor>? BuildScenarios()
    {
        if (_scenarios is not { } scenarios) return null;

        var cases = new Dictionary<string, PortExecutor>(StringComparer.Ordinal);
        foreach (var (name, body) in scenarios.Cases)
            cases[name] = new PortExecutor(_portKey, Body(body), _factories);

        return cases;
    }

    /// <summary>场景主体（① 标准记录）→ impl 段（不携端口键与工厂表：前者随宿主，后者是装配产物）。</summary>
    private static PortImplStandard Body(ScenarioBodyStandard body)
        => new(
            PortImplVocabulary.KindName.Scripted,
            Binds: body.Binds,
            Deps: body.Deps,
            Script: body.Script,
            ScriptProject: body.Project,
            Deliver: body.Deliver,
            Scopes: body.Scopes);

    /// <summary>即时执行（主路径：建作用域 → 脚本 → 投影）。</summary>
    private object? Execute(IPortInput input)
    {
        var scope = Prepare(input);

        return WithScopes(input, scope, deps =>
        {
            ScriptedSteps.Run(_host, _program, scope, input, deps);

            if (_deliver.Value is { } value)
                return ScriptedExprEvaluator.Evaluate(value, scope, input, deps, $"载体值表达式（{_portKey}）");

            var product = Project(scope, input, deps);

            return _deliver.Issues ? new PortIssues(IssueList(IssueSource(product, scope))) : product;
        });
    }

    /// <summary>延迟步骤求值（<c>kind=action</c>：断言期才执行「步骤 + 投影」，与主执行共用求值路径与作用域语义）。</summary>
    private object? RunDeferred(IReadOnlyList<StepStandard> steps, IPortInput input)
    {
        var scope = Prepare(input);

        return WithScopes(input, scope, deps =>
        {
            Run(steps, scope, input, deps);
            return Project(scope, input, deps);
        });
    }

    /// <summary>建作用域 + 预建收集槽 + 输入绑定（即时 / 延迟 / issue 捕获三条路径共用的前置）。</summary>
    private EvalScope Prepare(IPortInput input)
    {
        // 每次求值开新执行流前清空留痕：并发 / 线程复用时不得读到上一次调用的记录。
        _lastNotes.Value = Array.Empty<string>();
        var scope = NewScope();
        foreach (var name in _collections) scope.Collect(name);
        BindInputs(scope, input);
        return scope;
    }

    /// <summary>
    /// 执行骨架（作用域 + 依赖 + 主体）：作用域在正常与异常路径按 LIFO 释放 ——
    /// 三条载体路径共用同一头尾，避免各自的释放语义漂移。
    /// </summary>
    private T WithScopes<T>(IPortInput input, EvalScope scope, Func<object?[], T> body)
    {
        var releases = new List<Action>();
        try
        {
            foreach (var declared in _scopes) releases.Add(Materialize(declared, scope, input));

            return body(ComposedDeps.Build(_deps, input, _factories, _portKey));
        }
        finally
        {
            Release(releases);
        }
    }

    /// <summary>
    /// issue 承载（<c>kind=issues</c> + 捕获体）：命中期望异常 → 一条 issue（消息 / 类型名两口径）；
    /// 未抛 → 空清单；非期望异常由共用调用路径原样上抛（不吞不换）。
    /// </summary>
    private IReadOnlyList<string> CaptureIssues(DeliverCaptureStandard capture, IPortInput input)
    {
        var scope = Prepare(input);

        return WithScopes(input, scope, deps =>
        {
            var context = $"载体 issues 的捕获调用 '{capture.Call.Method}'";

            if (capture.When is { } gate
                && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(gate, scope, input, deps, context)))
            {
                return Array.Empty<string>();
            }

            var call = capture.Call with { Expect = ExpectDeclaration(capture) };
            var result = ScriptedSteps.RunInvoke(
                _host, new PortInstr(PortOp.Call, Operand: call), call, scope, input, deps);
            if (result is not Exception exception) return Array.Empty<string>();

            return IssueText(capture, exception, scope, context);
        });
    }

    /// <summary>issue 文本口径（<c>messageField</c> 非空 → 取该槽文本；否则按 <c>message</c> 取消息 / 类型名）。</summary>
    private static IReadOnlyList<string> IssueText(
        DeliverCaptureStandard capture, Exception exception, EvalScope scope, string context)
        => capture.MessageField is { Length: > 0 } slot
            ? [PortValues.Text(scope.Slot(slot, context)) ?? string.Empty]
            : [string.Equals(capture.Message, "TypeName", StringComparison.Ordinal)
                ? exception.GetType().Name
                : exception.Message];

    /// <summary>捕获体期望类型声明（<c>expect</c> 集 → 管道串；空集 → 不捕获任何异常，即恒空清单）。</summary>
    private static string ExpectDeclaration(DeliverCaptureStandard capture)
        => capture.Call.Expect is { Length: > 0 } declared ? declared : string.Join('|', capture.Expect);

    /// <summary>输入绑定求值（声明序 = 求值序；依赖尚未构建，故一律无 dep 源）。</summary>
    private void BindInputs(EvalScope scope, IPortInput input)
    {
        foreach (var (name, bind) in _binds)
            scope.Set(name, ScriptedValue.ResolveTyped(
                bind, typeof(object), scope, input, NoDeps, $"输入绑定 '{name}'"));
    }

    /// <summary>投影（声明序 = 产物字段序；<c>spread</c> 自占位不写入字段）。</summary>
    private Dictionary<string, object?> Project(EvalScope scope, IPortInput input, object?[] deps)
    {
        var product = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _project) Project(field, product, scope, input, deps);
        return product;
    }

    /// <summary>载体种类（<c>kind</c> 缺省时按旧布尔推断：<c>defer</c> → action；<c>issues</c> → issues）。</summary>
    private static string KindOf(DeliverStandard deliver)
        => deliver.Kind is { Length: > 0 } kind
            ? kind
            : deliver.Defer ? "action" : deliver.Issues ? "issues" : "value";

    /// <summary>载体形态装配期校验（取值域 + 载体单一性）。</summary>
    private void ValidateKind()
    {
        var kind = KindOf(_deliver);
        if (kind is not ("value" or "issues" or "action"))
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的载体 kind '{kind}' 非法（value|issues|action）");

        string? conflict = null;
        if (kind == "value" && (_deliver.Capture is not null || _deliver.DeferSteps is { Count: > 0 }))
            conflict = "issue 捕获体 / 延迟步骤";
        else if (kind == "issues" && _deliver.DeferSteps is { Count: > 0 })
            conflict = "延迟步骤";
        else if (kind == "action" && _deliver.Capture is not null)
            conflict = "issue 捕获体";

        if (conflict is not null)
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的载体 kind='{kind}' 与 {conflict} 互斥（载体形态单一）");

        if (_deliver.Value is not null && kind != "value")
            throw new PortBindingViolationException(
                _portKey,
                $"端口 '{_portKey}' 的 deliver.value（值载体表达式）仅与 kind='value' 同用（当前 kind='{kind}'）");
    }

    /// <summary>作用域声明装配期校验（种类 / 作用域名唯一 / 各类必填项 / 文件名规范）。</summary>
    private void ValidateScopes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in _scopes)
        {
            if (scope.Kind is not ("tempDir" or "using" or "restore"))
                throw new PortBindingViolationException(
                    _portKey,
                    $"端口 '{_portKey}' 的作用域 kind '{scope.Kind}' 非法（tempDir|using|restore）");

            if (string.IsNullOrWhiteSpace(scope.As) || !names.Add(scope.As))
                throw new PortBindingViolationException(
                    _portKey, $"端口 '{_portKey}' 的作用域名 '{scope.As}' 为空或重复（须唯一）");

            if (scope.Kind == "using" && scope.Invoke is null)
                throw new PortBindingViolationException(
                    _portKey, $"端口 '{_portKey}' 的 using 作用域 '{scope.As}' 缺 invoke（进入期调用）");

            if (scope.Kind == "restore"
                && scope.Open is not { Count: > 0 } && scope.Close is not { Count: > 0 }
                && !(scope.Save ?? Array.Empty<SaveStandard>()).Any(save => save.Set is not null || save.Restore))
            {
                throw new PortBindingViolationException(
                    _portKey,
                    $"端口 '{_portKey}' 的 restore 作用域 '{scope.As}' 的 open / close 步骤均为空" +
                    "且 save 未声明 set / restore（无还原语义）");
            }

            if (scope.Kind != "tempDir" && scope.Files is { Count: > 0 })
                throw new PortBindingViolationException(
                    _portKey,
                    $"端口 '{_portKey}' 的作用域 '{scope.As}'（kind='{scope.Kind}'）不支持 files（预置文件仅 tempDir）");

            foreach (var file in scope.Files ?? Array.Empty<FileStandard>())
            {
                if (file.Batch is not null)
                {
                    if (file.Name is not null || file.NameExpr is null)
                        throw new PortBindingViolationException(
                            _portKey,
                            $"端口 '{_portKey}' 的作用域 '{scope.As}' 的文件批形态非法" +
                            "（batch 与 name 字面量互斥；批量须声明 name 表达式）");

                    if (file.From is not null)
                        throw new PortBindingViolationException(
                            _portKey,
                            $"端口 '{_portKey}' 的作用域 '{scope.As}' 的文件批不接受 from（逐元素内容用 content 表达式）");

                    continue;
                }

                if (file.NameExpr is not null)
                    throw new PortBindingViolationException(
                        _portKey,
                        $"端口 '{_portKey}' 的作用域 '{scope.As}' 的文件项声明了 name 表达式但缺 batch（批量形态须给 batch）");

                if (string.IsNullOrWhiteSpace(file.Name) || file.Name.IndexOfAny(['\\', '/', ':']) >= 0)
                    throw new PortBindingViolationException(
                        _portKey,
                        $"端口 '{_portKey}' 的作用域 '{scope.As}' 的文件名 '{file.Name}' 非法" +
                        "（须非空 ∧ 不含路径分隔符；子目录用 dir 表达式声明）");
            }
        }
    }

    /// <summary>
    /// 多场景声明装配期校验（W2：场景集非空 ∧ 场景名非空）。
    /// <para>
    /// 归一批 I5a：③ 场景主体自带端口键，故旧校验含「主体端口键 ≡ 宿主」一项；① 场景主体
    /// （<see cref="ScenarioBodyStandard"/>）**不含端口键**（键随宿主继承，见 <see cref="BuildScenarios"/>），
    /// 该不变式已成为结构事实 —— 校验项随之删除（非放宽：不一致态在 ① 形态下不可表达）。
    /// </para>
    /// </summary>
    private void ValidateScenarios()
    {
        if (_scenarios is not { } scenarios) return;

        if (scenarios.Cases.Count == 0)
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 声明了 scenarios 但场景集为空（无可分派主体）");

        foreach (var name in scenarios.Cases.Keys)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new PortBindingViolationException(
                    _portKey, $"端口 '{_portKey}' 的 scenarios 含空场景名");
        }
    }

    /// <summary>建作用域并返回其**释放动作**（LIFO 逆序执行；正常与异常路径均执行；释放动作本身抛错即上抛）。</summary>
    private Action Materialize(ScopeStandard declared, EvalScope scope, IPortInput input)
    {
        if (declared.Kind == "using") return EnterUsing(declared, scope, input);
        if (declared.Kind == "restore") return EnterRestore(declared, scope, input);
        if (declared.Kind == "tempDir") return EnterTempDir(declared, scope, input);

        throw new PortBindingViolationException(
            _portKey,
            $"端口 '{_portKey}' 的作用域 kind '{declared.Kind}' 非法（tempDir|using|restore）");
    }

    /// <summary>资源释放作用域（W18：<c>using</c>）：进入期执行 <see cref="ScopeStandard.Invoke"/>，
    /// 结果写槽 <see cref="ScopeStandard.As"/> 并要求可释放；退出（含异常路径）按 <c>IDisposable</c> / <c>IAsyncDisposable</c> 释放。</summary>
    private Action EnterUsing(ScopeStandard declared, EvalScope scope, IPortInput input)
    {
        var invoke = declared.Invoke ?? throw new PortBindingViolationException(
            _portKey, $"端口 '{_portKey}' 的 using 作用域 '{declared.As}' 缺 invoke（进入期调用）");

        var outcome = ScriptedInvokeRunner.Run(_portKey, invoke, _deps, scope, input, NoDeps);
        var value = outcome.Result;
        scope.Set(declared.As, value);

        if (value is null)
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的 using 作用域 '{declared.As}' 的进入期调用返回 null（不可释放）");

        return value switch
        {
            IDisposable disposable => disposable.Dispose,
            IAsyncDisposable asyncDisposable => () =>
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult(),
            _ => throw new PortBindingViolationException(
                _portKey,
                $"端口 '{_portKey}' 的 using 作用域 '{declared.As}' 的进入期结果 {value.GetType().Name} 不可释放（须 IDisposable / IAsyncDisposable）"),
        };
    }

    /// <summary>
    /// 静态槽还原作用域（W27 / NG-2：<c>restore</c>）：留存静态成员当前值 → （声明 <c>set</c> 时）进入期设值 →
    /// 执行进入期步骤 → 退出（含异常路径）执行退出期步骤 → （声明 <c>restore</c> 时）自动写回留存值。
    /// <para>作用域步骤**不得引用依赖**（依赖在其后构建），引用即抛；这是刻意的顺序约束（依赖构建可能需作用域内路径）。</para>
    /// </summary>
    private Action EnterRestore(ScopeStandard declared, EvalScope scope, IPortInput input)
    {
        var saved = new List<(SaveStandard Declared, MemberInfo Member)>();

        foreach (var save in declared.Save ?? Array.Empty<SaveStandard>())
        {
            var type = ComposedDeps.ResolveType(save.Type, save.GenericArgs);
            var member = ReflectivePortResolver.ResolveMember(type, _portKey, save.Key, isStatic: true);

            scope.Set(save.Slot, ReflectivePortResolver.ReadMember(member, instance: null));
            saved.Add((save, member));

            if (save.Set is { } declaredValue)
            {
                var context = $"作用域 '{declared.As}' 的 save 项 '{save.Slot}'（进入期设值）";
                ReflectivePortResolver.WriteMember(member, instance: null,
                    ScriptedValue.ResolveTyped(
                        declaredValue, ReflectivePortResolver.MemberType(member), scope, input, NoDeps, context));
            }
        }

        Run(declared.Open ?? Array.Empty<StepStandard>(), scope, input, NoDeps);

        return () =>
        {
            Run(declared.Close ?? Array.Empty<StepStandard>(), scope, input, NoDeps);

            foreach (var (save, member) in saved)
            {
                if (!save.Restore) continue;
                ReflectivePortResolver.WriteMember(member, instance: null,
                    scope.Slot(save.Slot, $"作用域 '{declared.As}' 的 save 项 '{save.Slot}'（退出期还原）"));
            }
        };
    }

    /// <summary>
    /// 建临时目录作用域（<c>tempDir</c>）：建目录 → 登记路径 → 按序写预置文件（单件 / 批量 NG-1）；释放 = 递归清理目录。
    /// </summary>
    private Action EnterTempDir(ScopeStandard declared, EvalScope scope, IPortInput input)
    {
        var prefix = declared.Prefix is { Length: > 0 } name ? name : "spec";
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

        System.IO.Directory.CreateDirectory(directory);
        scope.SetTempDir(declared.As, directory);

        foreach (var file in declared.Files ?? Array.Empty<FileStandard>())
        {
            if (file.Batch is { } batch)
            {
                var frame = file.As is { Length: > 0 } elementName ? elementName : "item";
                var source = ScriptedValue.Resolve(
                    batch, scope, input, NoDeps, $"作用域 '{declared.As}' 的文件批（batch 集合来源）");

                var index = 0;
                foreach (var element in PortValues.Elements(source))
                {
                    scope.Push(frame, element, index);
                    try
                    {
                        var context = $"作用域 '{declared.As}' 的文件批 '{frame}[{index}]'";
                        var batchName = PortValues.Text(
                            ScriptedExprEvaluator.Evaluate(file.NameExpr!, scope, input, NoDeps, context));
                        WriteFile(directory, declared.As, batchName ?? string.Empty, file, scope, input, context);
                    }
                    finally
                    {
                        scope.Pop();
                    }

                    index++;
                }

                continue;
            }

            WriteFile(
                directory, declared.As, file.Name ?? string.Empty, file, scope, input,
                $"作用域 '{declared.As}' 的文件 '{file.Name}'");
        }

        return () => TryDelete(directory);
    }

    /// <summary>写单个预置文件（门控 → 子目录表达式 → 内容；文件名须非空且不含路径分隔符，fail-fast）。</summary>
    private void WriteFile(
        string directory, string asName, string name, FileStandard file,
        EvalScope scope, IPortInput input, string context)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new PortBindingViolationException(
                _portKey,
                $"端口 '{_portKey}' 的作用域 '{asName}' 的文件名 '{name}' 非法" +
                "（须非空 ∧ 不含路径分隔符；子目录用 dir 表达式声明）");

        if (file.When is { } gate
            && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(gate, scope, input, NoDeps, context)))
        {
            return;
        }

        var root = file.Dir is { } relative
            ? System.IO.Path.Combine(
                directory,
                PortValues.Text(
                    ScriptedExprEvaluator.Evaluate(relative, scope, input, NoDeps, context)) ?? string.Empty)
            : directory;

        System.IO.Directory.CreateDirectory(root);

        var content = file.Content is { } body
            ? PortValues.Text(ScriptedExprEvaluator.Evaluate(body, scope, input, NoDeps, context))
            : file.From is { } from
                ? PortValues.Text(ScriptedValue.Resolve(from, scope, input, NoDeps, context))
                : null;

        System.IO.File.WriteAllText(System.IO.Path.Combine(root, name), content ?? string.Empty);
    }

    /// <summary>释放序列（LIFO 逆序；单个释放失败不掩盖其余释放：先跑完全部释放动作，末尾上抛首个异常）。</summary>
    private static void Release(IReadOnlyList<Action> releases)
    {
        Exception? failure = null;
        for (var i = releases.Count - 1; i >= 0; i--)
        {
            try
            {
                releases[i]();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>建作用域（统一注入委托工厂钩子，W26：<c>from:delegate</c> 的委托体需执行声明步骤）。</summary>
    private EvalScope NewScope()
    {
        var scope = new EvalScope(_portKey);
        scope.Delegates = BuildDelegate;
        return scope;
    }

    /// <summary>递归清理作用域目录（清理失败不掩盖原始异常：保留现场供排查）。</summary>
    private static void TryDelete(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (System.IO.IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 按声明序执行步骤：③ 声明经 <see cref="PortSpecCompiler.Steps"/> 映射为指令后交执行单源
    /// <see cref="ScriptedSteps"/>；本件不再持有步骤语义（单一实现点，防两套语义分叉）。
    /// </summary>
    private object? Run(IReadOnlyList<StepStandard> steps, EvalScope scope, IPortInput input, object?[] deps)
        => ScriptedSteps.Run(_host, PortSpecCompiler.Steps(steps), scope, input, deps);

    /// <summary>投影：字段 ← 表达式；<c>spread</c> 形态改为动态键逐项写入产物（无字段名的 <c>spread</c> 项 = 纯动态键投影）。</summary>
    private void Project(
        FieldExprStandard field, Dictionary<string, object?> product,
        EvalScope scope, IPortInput input, object?[] deps)
    {
        var notes = scope.NoteCount;
        _ = scope.TakeBottom();   // ⊥ 出处逐字段隔离（防上一字段的 ⊥ 误归到本字段）
        var value = ScriptedExprEvaluator.Evaluate(field.Expr, scope, input, deps, field.Field);

        // E8：⊥（运行期未写入的槽 / 帧）用于投影字段 → **该字段不产出**（并留痕），不得静默产 null。
        if (value is null && field.Expr.Spread is null && scope.NoteCount == notes
            && scope.TakeBottom() is { } provenance)
        {
            scope.Note($"端口 '{scope.PortKey}' 投影字段 '{field.Field}' 为 ⊥（{provenance}）：该字段不产出");
            return;
        }

        if (field.Expr.Spread is not null)
        {
            if (value is IReadOnlyDictionary<string, object?> entries)
            {
                foreach (var (key, item) in entries) product[key] = item;
                return;
            }

            if (field.Field.Length == 0)
                throw new PortBindingViolationException(
                    _portKey,
                    $"端口 '{_portKey}' 的动态键投影（无字段名）求值结果不是字典（不可展开为产物键）");
        }

        if (field.Field.Length == 0)
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的投影项既无字段名亦非动态键投影（spread）");

        product[field.Field] = value;
    }

    /// <summary>清单来源：声明字段（槽优先，回落产物字段）→ 首个产物字段。</summary>
    private object? IssueSource(Dictionary<string, object?> product, EvalScope scope)
    {
        if (_deliver.IssueField is { Length: > 0 } name)
        {
            if (scope.Has(name)) return scope.Slot(name, "载体 issues");
            if (product.TryGetValue(name, out var declared)) return declared;

            throw new PortBindingViolationException(
                _portKey,
                $"端口 '{_portKey}' 的载体 issues 引用了未定义的字段 / 槽 '{name}'");
        }

        return product.Count > 0 ? product.Values.ToArray() : null;
    }

    /// <summary>问题清单归一：异常 → 其消息；集合 → 逐项文本；null → 空清单；其余 → 单项文本。</summary>
    private static IReadOnlyList<string> IssueList(object? value)
        => value switch
        {
            null => Array.Empty<string>(),
            string text => new[] { text },
            Exception exception => new[] { exception.Message },
            _ => PortValues.Elements(value).Select(item => PortValues.Text(item) ?? string.Empty).ToArray(),
        };

    /// <summary>
    /// 委托工厂（W26）：闭合委托类型 → 生成「调用期执行声明步骤」的委托（<c>System.Linq.Expressions</c> 编译，
    /// 无需 IL 发射 / 源生成器）。
    /// <para>闭包语义：委托体复用**创建时**作用域（外层槽与收集槽可见）⇒ 体内 <c>collect</c> 即调用期留痕；
    /// 委托实参以帧名注入（<c>parameters</c> 声明名，缺省 <c>arg0</c>/<c>arg1</c>/…），体内 <c>from:item</c> 取之。</para>
    /// </summary>
    private object? BuildDelegate(
        DelegateStandard declared, EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        var typeName = declared.Kind is { Length: > 0 } kind
            ? kind
            : throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的 {context} 的委托声明缺 kind（委托类型全名）");

        var delegateType = ComposedDeps.ResolveType(typeName, null);
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的 {context} 的 kind '{typeName}' 不是委托类型");

        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的 {context} 的委托类型 '{delegateType.Name}' 缺 Invoke 方法");

        var parameters = invoke.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();

        var body = new ScriptedDelegateBody(this, declared, scope, input, deps, invoke.ReturnType);
        var call = Expression.Call(
            Expression.Constant(body),
            typeof(ScriptedDelegateBody).GetMethod(nameof(ScriptedDelegateBody.Call))!,
            Expression.NewArrayInit(
                typeof(object), parameters.Select(parameter => (Expression)Expression.Convert(parameter, typeof(object)))));

        var lambda = invoke.ReturnType == typeof(void)
            ? (Expression)Expression.Block(call, Expression.Empty())
            : Expression.Convert(call, invoke.ReturnType);

        return Expression.Lambda(delegateType, lambda, parameters).Compile();
    }

    /// <summary>
    /// 委托**调用体**（W26）：注入实参帧 → 执行声明步骤 → 按声明返回类型归一并返回（<c>Task</c> 类返回按需包装）。
    /// </summary>
    private sealed class ScriptedDelegateBody
    {
        private readonly PortExecutor _owner;
        private readonly DelegateStandard _declared;
        private readonly EvalScope _scope;
        private readonly IPortInput _input;
        private readonly object?[] _deps;
        private readonly Type _returnType;

        internal ScriptedDelegateBody(
            PortExecutor owner, DelegateStandard declared, EvalScope scope,
            IPortInput input, object?[] deps, Type returnType)
        {
            _owner = owner;
            _declared = declared;
            _scope = scope;
            _input = input;
            _deps = deps;
            _returnType = returnType;
        }

        /// <summary>调用入口（表达式树调用目标；实参个数由委托签名决定）。</summary>
        public object? Call(object?[] args)
        {
            var context = "委托调用体";

            for (var i = 0; i < args.Length; i++) _scope.Push(FrameName(i), args[i], i);

            try
            {
                if (_declared.Collect is { Length: > 0 } slot) _scope.Collect(slot).Add(args);

                var last = _owner.Run(_declared.Steps ?? Array.Empty<StepStandard>(), _scope, _input, _deps);
                var produced = _declared.Result is { } expression
                    ? ScriptedExprEvaluator.Evaluate(expression, _scope, _input, _deps, context)
                    : last;

                return Align(produced);
            }
            finally
            {
                for (var i = 0; i < args.Length; i++) _scope.Pop();
            }
        }

        /// <summary>实参帧名（<c>parameters</c> 声明名优先，缺省 <c>argN</c>）。</summary>
        private string FrameName(int index)
            => _declared.Parameters is { Count: > 0 } names && index < names.Count ? names[index] : $"arg{index}";

        /// <summary>返回值归一到声明返回类型（<c>await</c> 折取 / 任务包装单源 <see cref="PortInvocation.Realize"/>）。</summary>
        private object? Align(object? produced)
        {
            var value = _declared.Await && produced is Task pending ? PortInvocation.Fold(pending) : produced;

            return PortInvocation.Realize(value, _returnType);
        }
    }
}

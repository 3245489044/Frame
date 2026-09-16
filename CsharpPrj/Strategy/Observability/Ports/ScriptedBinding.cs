using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 声明式**脚本绑定**求值器（消除通道 C5）：按声明序执行「输入绑定 → 依赖构建 → 脚本步骤 → 投影 → 载体」，
/// 产出 <c>字段 → 值</c> 字典（与旧夹具编排逐字等价）。
/// <para>定位：与 <see cref="ComposedBinding"/> 并列的机制件——差别为「可遍历用例声明的数据 + 可用上一步结果再调 +
/// 一般取值表达式 + 载体形态」；同为零领域词汇（类型 / 方法 / 字段名一律不透明字符串）。</para>
/// <para>依赖构建与调用折叠复用单源（<see cref="ComposedDeps"/> / <see cref="PortInvocation"/>）。</para>
/// </summary>
public sealed class ScriptedBinding : IPortBinding
{
    private readonly ScriptedBindingSpec _spec;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private static readonly object?[] NoDeps = Array.Empty<object?>();

    /// <summary>声明的收集槽名集（步骤 / 遍历 / 投影 collect / sameAsLast）。</summary>
    private readonly IReadOnlySet<string> _collections;

    /// <summary>按声明构造绑定：无行为声明、载体冲突、引用形态残缺 → 即抛（fail-fast，防静默绑错）。</summary>
    public ScriptedBinding(ScriptedBindingSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _factories = spec.Factories;
        _collections = Collections(spec);

        if (spec.Script.Count == 0 && spec.Project.Count == 0 && spec.Deliver.Capture is null
            && spec.Deliver.DeferSteps is not { Count: > 0 } && spec.Deliver.Value is null
            && spec.Scenarios is null)
        {
            throw new PortBindingViolationException(
                spec.PortKey,
                $"端口 '{spec.PortKey}' 声明 scripted 绑定但 script / project / 延迟步骤 / issue 捕获体 / 值载体表达式均为空（无行为可执行）");
        }

        if (spec.Deliver.Defer && spec.Deliver.Issues)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的载体 defer 与 issues 互斥（二选一）");

        ValidateKind(spec);
        ValidateScopes(spec);
        ValidateScenarios(spec);
    }

    /// <summary>最近一次求值的诊断记录（W1：步骤 <c>when</c> / 遍历 <c>where</c> 求值为 ⊥ ⇒ 不执行且留痕，不静默）。</summary>
    public IReadOnlyList<string> LastNotes { get; private set; } = Array.Empty<string>();

    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(Invoke(input));

    /// <summary>求值（本件唯一执行序）：场景分派 → bind → scopes → deps → script → project → deliver。</summary>
    private object? Invoke(IPortInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_spec.Scenarios is { } scenarios) return Dispatch(scenarios, input);

        return KindOf(_spec.Deliver) switch
        {
            "action" => new DeferredAction(() => _spec.Deliver.DeferSteps is { Count: > 0 } steps
                ? RunDeferred(steps, input)
                : Execute(input)),

            "issues" when _spec.Deliver.Capture is { } capture
                => new PortIssues(CaptureIssues(capture, input)),

            _ => Execute(input),
        };
    }

    /// <summary>
    /// 多场景探针分派（W2：<c>PortProbeRouter.Declare</c> 的一等声明等价）：取场景名 → 命中即执行该场景主体
    /// （独立绑定实例，共享同一端口键与工厂表）；**未命中即抛**，诊断列全部可用场景名（不静默回落）。
    /// </summary>
    private object? Dispatch(ScriptedScenarioSpec scenarios, IPortInput input)
    {
        var probe = NewScope();
        var chosen = PortValues.Text(ScriptedValue.Resolve(
            scenarios.Select with { Optional = true }, probe, input, NoDeps, "场景选择（select）"));

        if (chosen is not { Length: > 0 } || !scenarios.Cases.TryGetValue(chosen, out var body))
        {
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的探针 '{chosen ?? "<空>"}' 未命中" +
                $"（可用场景：{string.Join(", ", scenarios.Cases.Keys.OrderBy(name => name, StringComparer.Ordinal))}）");
        }

        return new ScriptedBinding(body).Invoke(input);
    }

    /// <summary>即时执行（主路径：建作用域 → 脚本 → 投影；作用域在正常与异常路径均按 LIFO 释放）。</summary>
    private object? Execute(IPortInput input)
    {
        var scope = NewScope();

        foreach (var name in _collections) scope.Collect(name);

        BindInputs(scope, input);

        var releases = new List<Action>();
        try
        {
            foreach (var declared in _spec.Scopes) releases.Add(Materialize(declared, scope, input));

            var deps = ComposedDeps.Build(_spec.Deps, input, _factories, _spec.PortKey);

            Run(_spec.Script, scope, input, deps);

            if (_spec.Deliver.Value is { } value)
                return ScriptedExprEvaluator.Evaluate(value, scope, input, deps, $"载体值表达式（{_spec.PortKey}）");

            var product = Project(scope, input, deps);

            return _spec.Deliver.Issues ? new PortIssues(IssueList(IssueSource(product, scope))) : product;
        }
        finally
        {
            Release(releases);
        }
    }

    /// <summary>延迟步骤求值（<c>kind=action</c>：断言期才执行「步骤 + 投影」，与主执行共用同一求值路径）。</summary>
    private object? RunDeferred(IReadOnlyList<ScriptedStep> steps, IPortInput input)
    {
        var scope = NewScope();

        foreach (var name in _collections) scope.Collect(name);

        BindInputs(scope, input);

        var deps = ComposedDeps.Build(_spec.Deps, input, _factories, _spec.PortKey);

        Run(steps, scope, input, deps);
        return Project(scope, input, deps);
    }

    /// <summary>
    /// issue 承载（<c>kind=issues</c> + 捕获体）：命中期望异常 → 一条 issue（消息 / 类型名两口径）；
    /// 未抛 → 空清单；非期望异常由共用调用路径原样上抛（不吞不换）。
    /// </summary>
    private IReadOnlyList<string> CaptureIssues(ScriptedDeliverCaptureSpec capture, IPortInput input)
    {
        var scope = NewScope();

        foreach (var name in _collections) scope.Collect(name);

        BindInputs(scope, input);

        var releases = new List<Action>();
        try
        {
            foreach (var declared in _spec.Scopes) releases.Add(Materialize(declared, scope, input));

            var deps = ComposedDeps.Build(_spec.Deps, input, _factories, _spec.PortKey);
            var context = $"载体 issues 的捕获调用 '{capture.Call.Method}'";

            if (capture.When is { } gate
                && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(gate, scope, input, deps, context)))
            {
                return Array.Empty<string>();
            }

            var call = capture.Call with { Expect = ExpectDeclaration(capture) };
            var result = RunInvoke(new ScriptedStep(Invoke: call), call, scope, input, deps);
            if (result is not Exception exception) return Array.Empty<string>();

            return
            [
                string.Equals(capture.Message, "TypeName", StringComparison.Ordinal)
                    ? exception.GetType().Name
                    : exception.Message,
            ];
        }
        finally
        {
            Release(releases);
        }
    }

    /// <summary>捕获体期望类型声明（<c>expect</c> 集 → 管道串；空集 → 不捕获任何异常，即恒空清单）。</summary>
    private static string ExpectDeclaration(ScriptedDeliverCaptureSpec capture)
        => capture.Call.Expect is { Length: > 0 } declared ? declared : string.Join('|', capture.Expect);

    /// <summary>输入绑定求值（声明序 = 求值序；依赖尚未构建，故一律无 dep 源）。</summary>
    private void BindInputs(ScriptedScope scope, IPortInput input)
    {
        foreach (var (name, bind) in _spec.Binds)
            scope.Set(name, ScriptedValue.ResolveTyped(
                bind, typeof(object), scope, input, NoDeps, $"输入绑定 '{name}'"));
    }

    /// <summary>投影（声明序 = 产物字段序；<c>spread</c> 自占位不写入字段）。</summary>
    private Dictionary<string, object?> Project(ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var product = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _spec.Project) Project(field, product, scope, input, deps);
        return product;
    }

    /// <summary>载体种类（<c>kind</c> 缺省时按旧布尔推断：<c>defer</c> → action；<c>issues</c> → issues）。</summary>
    private static string KindOf(ScriptedDeliverSpec deliver)
        => deliver.Kind is { Length: > 0 } kind
            ? kind
            : deliver.Defer ? "action" : deliver.Issues ? "issues" : "value";

    /// <summary>载体形态装配期校验（取值域 + 载体单一性）。</summary>
    private static void ValidateKind(ScriptedBindingSpec spec)
    {
        var kind = KindOf(spec.Deliver);
        if (kind is not ("value" or "issues" or "action"))
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的载体 kind '{kind}' 非法（value|issues|action）");

        var conflict = kind switch
        {
            "value" when spec.Deliver.Capture is not null || spec.Deliver.DeferSteps is { Count: > 0 }
                => "issue 捕获体 / 延迟步骤",
            "issues" when spec.Deliver.DeferSteps is { Count: > 0 } => "延迟步骤",
            "action" when spec.Deliver.Capture is not null => "issue 捕获体",
            _ => null,
        };

        if (conflict is not null)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的载体 kind='{kind}' 与 {conflict} 互斥（载体形态单一）");

        if (spec.Deliver.Value is not null && kind != "value")
            throw new PortBindingViolationException(
                spec.PortKey,
                $"端口 '{spec.PortKey}' 的 deliver.value（值载体表达式）仅与 kind='value' 同用（当前 kind='{kind}'）");
    }

    /// <summary>作用域声明装配期校验（种类 / 作用域名唯一 / 各类必填项 / 文件名规范）。</summary>
    private static void ValidateScopes(ScriptedBindingSpec spec)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in spec.Scopes)
        {
            if (scope.Kind is not ("tempDir" or "using" or "restore"))
                throw new PortBindingViolationException(
                    spec.PortKey,
                    $"端口 '{spec.PortKey}' 的作用域 kind '{scope.Kind}' 非法（tempDir|using|restore）");

            if (string.IsNullOrWhiteSpace(scope.As) || !names.Add(scope.As))
                throw new PortBindingViolationException(
                    spec.PortKey, $"端口 '{spec.PortKey}' 的作用域名 '{scope.As}' 为空或重复（须唯一）");

            if (scope.Kind == "using" && scope.Invoke is null)
                throw new PortBindingViolationException(
                    spec.PortKey, $"端口 '{spec.PortKey}' 的 using 作用域 '{scope.As}' 缺 invoke（进入期调用）");

            if (scope.Kind == "restore"
                && scope.Open is not { Count: > 0 } && scope.Close is not { Count: > 0 })
            {
                throw new PortBindingViolationException(
                    spec.PortKey,
                    $"端口 '{spec.PortKey}' 的 restore 作用域 '{scope.As}' 的 open / close 步骤均为空（无还原语义）");
            }

            if (scope.Kind != "tempDir" && scope.Files is { Count: > 0 })
                throw new PortBindingViolationException(
                    spec.PortKey,
                    $"端口 '{spec.PortKey}' 的作用域 '{scope.As}'（kind='{scope.Kind}'）不支持 files（预置文件仅 tempDir）");

            foreach (var file in scope.Files ?? Array.Empty<ScriptedFileSpec>())
                if (string.IsNullOrWhiteSpace(file.Name) || file.Name.IndexOfAny(['\\', '/', ':']) >= 0)
                    throw new PortBindingViolationException(
                        spec.PortKey,
                        $"端口 '{spec.PortKey}' 的作用域 '{scope.As}' 的文件名 '{file.Name}' 非法" +
                        "（须非空 ∧ 不含路径分隔符；子目录用 dir 表达式声明）");
        }
    }

    /// <summary>多场景声明装配期校验（W2：场景集非空 ∧ 场景名非空 ∧ 场景主体端口键一致）。</summary>
    private static void ValidateScenarios(ScriptedBindingSpec spec)
    {
        if (spec.Scenarios is not { } scenarios) return;

        if (scenarios.Cases.Count == 0)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 声明了 scenarios 但场景集为空（无可分派主体）");

        foreach (var (name, body) in scenarios.Cases)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new PortBindingViolationException(
                    spec.PortKey, $"端口 '{spec.PortKey}' 的 scenarios 含空场景名");

            if (!string.Equals(body.PortKey, spec.PortKey, StringComparison.Ordinal))
                throw new PortBindingViolationException(
                    spec.PortKey,
                    $"端口 '{spec.PortKey}' 的场景 '{name}' 的端口键 '{body.PortKey}' 与宿主不一致");
        }
    }

    /// <summary>建作用域并返回其**释放动作**（LIFO 逆序执行；正常与异常路径均执行；释放动作本身抛错即上抛）。</summary>
    private Action Materialize(ScriptedScopeSpec declared, ScriptedScope scope, IPortInput input)
        => declared.Kind switch
        {
            "using" => EnterUsing(declared, scope, input),
            "restore" => EnterRestore(declared, scope, input),
            "tempDir" => EnterTempDir(declared, scope, input),
            _ => throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的作用域 kind '{declared.Kind}' 非法（tempDir|using|restore）"),
        };

    /// <summary>资源释放作用域（W18：<c>using</c>）：进入期执行 <see cref="ScriptedScopeSpec.Invoke"/>，
    /// 结果写槽 <see cref="ScriptedScopeSpec.As"/> 并要求可释放；退出（含异常路径）按 <c>IDisposable</c> / <c>IAsyncDisposable</c> 释放。</summary>
    private Action EnterUsing(ScriptedScopeSpec declared, ScriptedScope scope, IPortInput input)
    {
        var invoke = declared.Invoke ?? throw new PortBindingViolationException(
            _spec.PortKey, $"端口 '{_spec.PortKey}' 的 using 作用域 '{declared.As}' 缺 invoke（进入期调用）");

        var outcome = ScriptedInvokeRunner.Run(_spec.PortKey, invoke, _spec.Deps, scope, input, NoDeps);
        var value = outcome.Result;
        scope.Set(declared.As, value);

        if (value is null)
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的 using 作用域 '{declared.As}' 的进入期调用返回 null（不可释放）");

        return value switch
        {
            IDisposable disposable => disposable.Dispose,
            IAsyncDisposable asyncDisposable => () =>
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult(),
            _ => throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的 using 作用域 '{declared.As}' 的进入期结果 {value.GetType().Name} 不可释放（须 IDisposable / IAsyncDisposable）"),
        };
    }

    /// <summary>
    /// 静态槽还原作用域（W27：<c>restore</c>）：留存静态成员当前值 → 执行进入期步骤 → 退出（含异常路径）执行退出期步骤。
    /// <para>作用域步骤**不得引用依赖**（依赖在其后构建），引用即抛；这是刻意的顺序约束（依赖构建可能需作用域内路径）。</para>
    /// </summary>
    private Action EnterRestore(ScriptedScopeSpec declared, ScriptedScope scope, IPortInput input)
    {
        foreach (var save in declared.Save ?? Array.Empty<ScriptedSaveSpec>())
        {
            var type = ComposedDeps.ResolveType(save.Type, save.GenericArgs);
            var member = ReflectivePortResolver.ResolveMember(type, _spec.PortKey, save.Key, isStatic: true);

            scope.Set(save.Slot, member switch
            {
                PropertyInfo property => property.GetValue(null),
                FieldInfo field => field.GetValue(null),
                _ => null,
            });
        }

        Run(declared.Open ?? Array.Empty<ScriptedStep>(), scope, input, NoDeps);

        return () => Run(declared.Close ?? Array.Empty<ScriptedStep>(), scope, input, NoDeps);
    }

    /// <summary>建临时目录作用域（<c>tempDir</c>）：建目录 → 登记路径 → 按序写预置文件；释放 = 递归清理目录。</summary>
    private Action EnterTempDir(ScriptedScopeSpec declared, ScriptedScope scope, IPortInput input)
    {
        var prefix = declared.Prefix is { Length: > 0 } name ? name : "spec";
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

        System.IO.Directory.CreateDirectory(directory);
        scope.SetTempDir(declared.As, directory);

        foreach (var file in declared.Files ?? Array.Empty<ScriptedFileSpec>())
        {
            var context = $"作用域 '{declared.As}' 的文件 '{file.Name}'";

            if (file.When is { } gate
                && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(gate, scope, input, NoDeps, context)))
            {
                continue;
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

            System.IO.File.WriteAllText(System.IO.Path.Combine(root, file.Name), content ?? string.Empty);
        }

        return () => TryDelete(directory);
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
    private ScriptedScope NewScope()
    {
        var scope = new ScriptedScope(_spec.PortKey);
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

    /// <summary>按声明序执行步骤（<c>When</c> 为假即短路跳过；形态未知 → fail-fast）。返回末步产物值。</summary>
    private object? Run(IReadOnlyList<ScriptedStep> steps, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        object? last = null;

        foreach (var step in steps)
        {
            if (step.When is { } condition)
            {
                var gate = ScriptedExprEvaluator.Evaluate(condition, scope, input, deps, "步骤条件 when");

                // W1：⊥（缺槽 / 解析失败）语义 = 「不执行且记录」——留痕不静默（诊断面 LastNotes）。
                if (gate is null) scope.Note("步骤条件 when 求值为 ⊥（缺值 / 解析失败）：本步骤不执行");

                if (!PortValues.Truthy(gate)) continue;
            }

            if (step.New is { } construct) last = RunNew(step, construct, scope, input, deps);
            else if (step.Invoke is { } invoke) last = RunInvoke(step, invoke, scope, input, deps);
            else if (step.Foreach is { } loop) last = RunForeach(step, loop, scope, input, deps);
            else
                throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 的脚本步骤未声明形态（new|invoke|foreach）");
        }

        LastNotes = scope.Notes;
        return last;
    }

    /// <summary>构造步骤：反射构造（单源 <see cref="ScriptedConstruction"/>）→ 写槽 → 收集。返回构造对象。</summary>
    private object? RunNew(
        ScriptedStep step, ScriptedNewSpec construct, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var created = ScriptedConstruction.Build(construct, scope, input, deps, $"步骤 new '{construct.As}'");

        if (!string.IsNullOrWhiteSpace(construct.As)) scope.Set(construct.As, created);
        Collect(step, created, scope);
        return created;
    }

    /// <summary>调用步骤：执行（调用单源 <see cref="ScriptedInvokeRunner"/>）→ 写槽 / 出参捕获 / 收集 → 可选跳出遍历。</summary>
    private object? RunInvoke(
        ScriptedStep step, ScriptedInvokeSpec invoke, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var context = $"步骤 invoke '{invoke.Method}'";
        var outcome = ScriptedInvokeRunner.Run(_spec.PortKey, invoke, _spec.Deps, scope, input, deps);

        if (!invoke.IsProperty)
            ScriptedInvokeRunner.CaptureOut(_spec.PortKey, invoke, outcome.Parameters, outcome.Args, scope, context);

        if (invoke.As is { Length: > 0 } asName) scope.Set(asName, outcome.Result);

        // break 语义：声明 expect 时「命中即跳出」（首遇失败即停）；未声明 expect 时执行后即跳出。
        if (invoke.Break && ScriptedInvokeRunner.ShouldBreak(invoke, outcome.Result)) throw new BreakSignal();

        // 收集置于跳出判定之后：被 break 中止的迭代「未完成」→ 其收集不入列
        // （夹具「仅登记成功者入列」等口径由此对齐；foreach 级收集仍记录本次迭代末产物，见 RunForeach）。
        Collect(step, outcome.Result, scope);

        return outcome.Result;
    }

    /// <summary>遍历步骤：取集合（槽 / 表达式）逐项入栈循环变量 → 过滤 → 执行体内步骤（<c>break</c> 跳出本层）。返回末次迭代产物值。</summary>
    private object? RunForeach(
        ScriptedStep step, ScriptedForeachSpec loop, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        if (string.IsNullOrWhiteSpace(loop.As))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的遍历步骤缺少 as（循环变量名）");

        var context = $"遍历 '{loop.As}'";
        var items = loop.Slot is { Length: > 0 } slotName
            ? scope.Slot(slotName, context)
            : ScriptedExprEvaluator.Evaluate(
                loop.Items ?? throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 的 {context} 缺集合来源（slot 或 items）"),
                scope, input, deps, context);

        var body = loop.Do ?? Array.Empty<ScriptedStep>();
        var index = 0;
        object? last = null;

        foreach (var item in PortValues.Elements(items))
        {
            var produced = last;
            scope.Push(loop.As, item, index);
            try
            {
                var gate = loop.Where is { } filter
                    ? ScriptedExprEvaluator.Evaluate(filter, scope, input, deps, $"{context} 的过滤条件")
                    : true;

                if (loop.Where is not null && gate is null)
                    scope.Note($"{context} 的过滤条件求值为 ⊥（缺值 / 解析失败）：本次迭代不执行");

                if (PortValues.Truthy(gate)) produced = Run(body, scope, input, deps);
            }
            catch (BreakSignal)
            {
                scope.Pop();
                Collect(step, produced, scope);
                return produced;
            }

            scope.Pop();
            Collect(step, produced, scope);
            last = produced;
            index++;
        }

        return last;
    }

    /// <summary>投影：字段 ← 表达式；<c>spread</c> 形态改为动态键逐项写入产物（无字段名的 <c>spread</c> 项 = 纯动态键投影）。</summary>
    private void Project(
        ScriptedFieldSpec field, Dictionary<string, object?> product,
        ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var value = ScriptedExprEvaluator.Evaluate(field.Expr, scope, input, deps, field.Field);

        if (field.Expr.Spread is not null)
        {
            if (value is IReadOnlyDictionary<string, object?> entries)
            {
                foreach (var (key, item) in entries) product[key] = item;
                return;
            }

            if (field.Field.Length == 0)
                throw new PortBindingViolationException(
                    _spec.PortKey,
                    $"端口 '{_spec.PortKey}' 的动态键投影（无字段名）求值结果不是字典（不可展开为产物键）");
        }

        if (field.Field.Length == 0)
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的投影项既无字段名亦非动态键投影（spread）");

        product[field.Field] = value;
    }

    /// <summary>清单来源：声明字段（槽优先，回落产物字段）→ 首个产物字段。</summary>
    private object? IssueSource(Dictionary<string, object?> product, ScriptedScope scope)
    {
        if (_spec.Deliver.IssueField is { Length: > 0 } name)
        {
            if (scope.Has(name)) return scope.Slot(name, "载体 issues");
            if (product.TryGetValue(name, out var declared)) return declared;

            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的载体 issues 引用了未定义的字段 / 槽 '{name}'");
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

    /// <summary>收集声明（可多源：步产物 ∪ 指定槽当前值）。</summary>
    private static void Collect(ScriptedStep step, object? product, ScriptedScope scope)
    {
        if (step.Collect is not { Count: > 0 } collects) return;

        foreach (var collect in collects)
        {
            var value = collect.From is { Length: > 0 } source
                ? scope.Slot(source, $"步骤 collect 的 from 槽 '{source}'")
                : product;
            scope.Collect(collect.Slot).Add(value);
        }
    }

    /// <summary>遍历跳出信号（<c>break: true</c> → 跳出最近一层 <c>foreach</c>）。</summary>
    private sealed class BreakSignal : Exception;

    /// <summary>声明的收集槽名集（执行前预建空集合：短路 / 零迭代时投影仍可取空清单）。</summary>
    private static IReadOnlySet<string> Collections(ScriptedBindingSpec spec)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectSteps(spec.Script, names);

        foreach (var field in spec.Project) CollectExpr(field.Expr, names);

        return names;
    }

    private static void CollectSteps(IReadOnlyList<ScriptedStep> steps, HashSet<string> names)
    {
        foreach (var step in steps)
        {
            foreach (var collect in step.Collect ?? Array.Empty<ScriptedCollectSpec>()) names.Add(collect.Slot);
            if (step.Foreach is { } loop) CollectSteps(loop.Do ?? Array.Empty<ScriptedStep>(), names);
        }
    }

    private static void CollectExpr(ScriptedExpr expr, HashSet<string> names)
    {
        if (expr.Collect is { Length: > 0 } collect) names.Add(collect);
        if (expr.SameAsLast is { Length: > 0 } other) names.Add(other);

        foreach (var sub in Subs(expr)) CollectExpr(sub, names);
    }

    private static IEnumerable<ScriptedExpr> Subs(ScriptedExpr expr)
    {
        foreach (var candidate in new[]
        {
            expr.Count, expr.NotNull, expr.AsText, expr.TypeName, expr.Message, expr.Thrown,
            expr.Contains, expr.Of, expr.All, expr.Any, expr.AllIn, expr.Spread,
            expr.First, expr.FirstWhere, expr.IsNull, expr.Not, expr.Agg,
            expr.Filter, expr.FilterWhere,
            expr.Last, expr.LastWhere, expr.Dict, expr.DictKey, expr.DictValue, expr.DictWhere,
        })
        {
            if (candidate is not null) yield return candidate;
        }

        foreach (var item in expr.Concat ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.CountEqual ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.AllOf ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.AnyOf ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.Coalesce ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.SameReference ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.SequenceEqual ?? Array.Empty<ScriptedExpr>()) yield return item;

        if (expr.Distinct is { } distinct) yield return distinct;
        if (expr.Fold is { } fold) yield return fold;

        foreach (var part in expr.Parts ?? Array.Empty<ScriptedConcatPartSpec>())
        {
            yield return part.Expr;
            if (part.When is { } gate) yield return gate;
        }
    }

    /// <summary>
    /// 委托工厂（W26）：闭合委托类型 → 生成「调用期执行声明步骤」的委托（<c>System.Linq.Expressions</c> 编译，
    /// 无需 IL 发射 / 源生成器）。
    /// <para>闭包语义：委托体复用**创建时**作用域（外层槽与收集槽可见）⇒ 体内 <c>collect</c> 即调用期留痕；
    /// 委托实参以帧名注入（<c>parameters</c> 声明名，缺省 <c>arg0</c>/<c>arg1</c>/…），体内 <c>from:item</c> 取之。</para>
    /// </summary>
    private object? BuildDelegate(
        ScriptedDelegateSpec declared, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        var typeName = declared.Kind is { Length: > 0 } kind
            ? kind
            : throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的 {context} 的委托声明缺 kind（委托类型全名）");

        var delegateType = ComposedDeps.ResolveType(typeName, null);
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的 {context} 的 kind '{typeName}' 不是委托类型");

        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的 {context} 的委托类型 '{delegateType.Name}' 缺 Invoke 方法");

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
        private readonly ScriptedBinding _owner;
        private readonly ScriptedDelegateSpec _declared;
        private readonly ScriptedScope _scope;
        private readonly IPortInput _input;
        private readonly object?[] _deps;
        private readonly Type _returnType;

        internal ScriptedDelegateBody(
            ScriptedBinding owner, ScriptedDelegateSpec declared, ScriptedScope scope,
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

                var last = _owner.Run(_declared.Steps ?? Array.Empty<ScriptedStep>(), _scope, _input, _deps);
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

        /// <summary>返回值归一到声明返回类型（<c>await</c> 折取 / <c>Task</c> 包装）。</summary>
        private object? Align(object? produced)
        {
            var value = _declared.Await && produced is Task pending ? PortInvocation.Fold(pending) : produced;

            if (_returnType == typeof(Task)) return value as Task ?? Task.CompletedTask;

            if (_returnType.IsGenericType && _returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                if (value is Task) return value;

                var resultType = _returnType.GetGenericArguments()[0];
                var boxed = value ?? ReflectivePortResolver.DefaultFor(resultType);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType).Invoke(null, new[] { boxed });
            }

            return value;
        }
    }
}

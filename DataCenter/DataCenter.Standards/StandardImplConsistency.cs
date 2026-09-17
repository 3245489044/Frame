using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// 端口 <c>impl</c> 语义判据 C7–C14（标准框架通用化重构 §4.3/§5）：
/// 在 <c>PortStandard.Impl</c> 标准模型上直接求值，补 C1–C6「契约面」之外的「实现面」一致性。
/// <para>
/// 三条硬约束（防误杀 / 防双判，§5）：
/// ① 判据**只判跨项 / 跨字段一致性**——单项形状已在读取期（<see cref="StandardJson.ReadImpl"/>）守；
/// ② 判据口径对齐内核语义（执行器 / 装配器 / 对账器既有行为），禁自创；
/// ③ 判据零违例须在「产品台账 + 110 端口测试台账」上实测成立。
/// </para>
/// <para>
/// 边界（校正 #6 落实）：<c>kind/convert/from/select/fake/deliver.kind/scope.kind</c> 等**词表枚举**
/// 由结构门（<c>schemas\ports.schema.json</c>）单一承担，本件**不重复判枚举**——只判条件关系
/// （如 <c>from=arg → 须带 index</c>）与跨项引用（如 <c>project[].call ∈ calls[].as</c>），
/// 这类关系无法用结构 schema 表达、故留给语义判据。
/// </para>
/// </summary>
public static class StandardImplConsistency
{
    // 判据入口已规则化：C7–C14 由 StandardRuleSet 的规则类逐条调用本件判据体（宿主 = 框架 LedgerConsistency），
    // 此处不再保留并行入口（防同一规则两处表达）。

    // ---------------------------------------------------------------- C7

    /// <summary>C7：<c>impl.kind</c> 的必随字段——<c>reflective→target∧method</c>；<c>delegate→id</c>；<c>frozen→reason</c>。
    /// <para><c>kind∈词表</c> 由读取期 <c>NormalizeKind</c> 守，此处不重复；<c>impl</c> 缺失 ⇒ 视为 <c>delegate</c> 且 id=∅ ⇒ 判据消费，防「忘写实现」静默。</para>
    /// <para>实测量产校准：<c>reflective</c> 三形态均带 <c>target/method</c>（<c>id</c> 仅作 legacy 元数据，不妨碍本判据）。</para>
    /// </summary>
    internal static void CheckC7(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null || string.IsNullOrWhiteSpace(impl.Kind) || string.Equals(impl.Kind, "delegate", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(impl?.Id))
                violations.Add($"C7 端口[{port.Key}] 的 impl 缺实现（kind=delegate 但未给 id）——疑忘写实现");
            return;
        }

        if (string.Equals(impl.Kind, "reflective", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(impl.Target) || string.IsNullOrWhiteSpace(impl.Method))
                violations.Add($"C7 端口[{port.Key}] 的 impl.kind=reflective 须给 target 与 method（当前 target='{impl.Target}' method='{impl.Method}'）");
            return;
        }

        if (string.Equals(impl.Kind, "frozen", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(impl.Reason))
            violations.Add($"C7 端口[{port.Key}] 的 impl.kind=frozen 须给 reason（冻结须说明原因）");
    }

    // ---------------------------------------------------------------- C8

    /// <summary>C8：取值来源 <c>from</c> ↔ 伴生键 一致性（校对 #7：仅 <c>arg</c> 须带 <c>index</c>；<c>item</c> 配 <c>key</c>+<c>path</c>，不强制 index）。
    /// <para>枚举成员（<c>from</c>/<c>convert</c>）由结构门守，此处只判「选了某来源却缺其必随键」的条件关系。</para>
    /// </summary>
    internal static void CheckC8ValueSources(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null) return;

        WalkValueSources(impl, source =>
        {
            var from = source.From;
            if (string.IsNullOrWhiteSpace(from)) return;

            if (string.Equals(from, "arg", StringComparison.Ordinal) && source.Index is null)
                violations.Add($"C8 端口[{port.Key}] 的取值来源 from=arg 须带 index（当前缺）");
            else if (string.Equals(from, "with", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(source.Key))
                violations.Add($"C8 端口[{port.Key}] 的取值来源 from=with 须带 key（当前缺）");
            else if (string.Equals(from, "var", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(source.Key))
                violations.Add($"C8 端口[{port.Key}] 的取值来源 from=var 须带 key（当前缺）");
            else if (string.Equals(from, "dep", StringComparison.Ordinal) && source.Dep is null)
                violations.Add($"C8 端口[{port.Key}] 的取值来源 from=dep 须带 dep（当前缺）");
        });
    }

    // ---------------------------------------------------------------- C9

    /// <summary>C9（组合族）：<c>calls[].as</c> 唯一；<c>project[].call ∈ calls[].as</c>；<c>project[].select ∈ 词表</c>；
    /// <c>deps[i].args[*].dep &lt; i</c>（只引更早）；<c>project[].dep &lt; deps.Count</c>。
    /// <para><c>select∈词表</c> 因 <c>projectItem</c> 结构门开放（校正 #5）而无法由 schema 闭合，故此处判（非重复）。</para>
    /// </summary>
    internal static void CheckC9Composed(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null || !string.Equals(impl.Kind, "composed", StringComparison.Ordinal)) return;

        var deps = impl.Deps ?? [];
        var calls = impl.Calls ?? [];
        var project = impl.Project ?? [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            if (!seen.Add(call.As))
                violations.Add($"C9 端口[{port.Key}] 的 calls 别名 '{call.As}' 重复（须唯一）");
        }

        for (var i = 0; i < deps.Count; i++)
            foreach (var arg in deps[i].Args ?? [])
                if (arg.Dep is { } depIndex && depIndex >= i)
                    violations.Add($"C9 端口[{port.Key}] 的 deps[{i}].args 引用了 dep[{depIndex}]（只可引更早的依赖）");

        foreach (var field in project)
        {
            if (!string.IsNullOrWhiteSpace(field.Select)
                && !PortImplVocabulary.Selects.Contains(field.Select, StringComparer.Ordinal))
                violations.Add($"C9 端口[{port.Key}] 的 project['{field.Field}'].select='{field.Select}' 非法（非取值法词表）");

            if (string.Equals(field.Select, "fakeCount", StringComparison.Ordinal))
                continue; // fakeCount 由 dep+method+key 定位，call 可空（校正 #5）

            if (!string.IsNullOrWhiteSpace(field.Call) && !seen.Contains(field.Call))
                violations.Add($"C9 端口[{port.Key}] 的 project['{field.Field}'].call='{field.Call}' 未在 calls[].as 中声明");

            if (field.Dep is { } projectDep && projectDep >= deps.Count)
                violations.Add($"C9 端口[{port.Key}] 的 project['{field.Field}'].dep={projectDep} 越界（deps 共 {deps.Count} 项）");
        }
    }

    // ---------------------------------------------------------------- C10

    /// <summary>C10（脚本族）：步骤形态**恰一**（至多一个 <c>new/invoke/foreach</c>）；<c>foreach.do</c> 非空。
    /// <para>「至少一」由读取期 <c>ReadStep</c> 守；此处补「至多一」（读取期按 foreach→new→invoke 序静默取前者，双形态会被掩盖）。</para>
    /// <para>尚未接入（D2/D3 对齐前不接入，防误杀）：表达式「算子组恰一」、<c>collect</c> 引用⊆槽集——须先以
    /// <c>ScriptedExprEvaluator</c> 与脚本执行器逐组核对口径后放开。</para>
    /// </summary>
    internal static void CheckC10Scripted(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null || !string.Equals(impl.Kind, "scripted", StringComparison.Ordinal)) return;

        WalkSteps(impl, step =>
        {
            var forms = 0;
            if (step.New is not null) forms++;
            if (step.Invoke is not null) forms++;
            if (step.Foreach is not null) forms++;

            if (forms > 1)
                violations.Add($"C10 端口[{port.Key}] 的脚本步骤形态不止一个（new/invoke/foreach 出现了 {forms} 个），须恰一");
        });

        WalkForeaches(impl, foreachStep =>
        {
            if (foreachStep.Do is null || foreachStep.Do.Count == 0)
                violations.Add($"C10 端口[{port.Key}] 的 foreach 步骤缺 do（遍历体不得为空）");
        });
    }

    // ---------------------------------------------------------------- C11

    /// <summary>C11（脚本族载体/作用域）：<c>deliver.kind</c> 的子载体（value/capture/deferSteps）互斥；
    /// <c>from=tmpPath/tmpDir</c> 的取值来源须引用已声明的 <c>scopes[].as</c>。
    /// <para>（<c>deliver.kind</c>/<c>scope.kind</c> 枚举由结构门守，此处只判条件关系；实测台账 <c>scopes</c> 未使用，该分支由用例覆盖。）</para>
    /// </summary>
    internal static void CheckC11Deliver(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null || !string.Equals(impl.Kind, "scripted", StringComparison.Ordinal)) return;

        if (impl.Deliver is { } deliver)
        {
            var probes = 0;
            if (deliver.Value is not null) probes++;
            if (deliver.Capture is not null) probes++;
            if (deliver.DeferSteps is not null) probes++;
            if (probes > 1)
                violations.Add($"C11 端口[{port.Key}] 的 deliver 同时给了 {probes} 种载体（value/capture/deferSteps 须互斥）");
        }

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        CollectScopeNames(impl, scopes);

        WalkValueSources(impl, source =>
        {
            if (source.From is not ("tmpPath" or "tmpDir")) return;

            if (string.IsNullOrWhiteSpace(source.Scope) || !scopes.Contains(source.Scope))
                violations.Add($"C11 端口[{port.Key}] 的取值来源 from={source.From} 引用了作用域 '{source.Scope ?? "(未给)"}'（未在 scopes[].as 声明）");
        });
    }

    // ---------------------------------------------------------------- C12

    /// <summary>C12（依赖/替身形态）：<c>fake=∅</c> ⇒ contract/wrap/methods/values 全空；<c>fake≠∅</c> ⇒ contract 或 wrap 至少一个。
    /// <para>依赖形态三选一（new/factory/fake）由读取期 <c>ReadDeps</c> 守；<c>fake∈词表</c> 由结构门守。（实测台账无 fake 依赖，本判据由用例覆盖；口径对齐 <c>Fakes\PortFake.cs</c> 的 contract/wrap 形态要求。）</para>
    /// </summary>
    internal static void CheckC12Deps(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null) return;

        var depsWithIndex = new List<(DepStandard Dep, int Index)>();
        for (var i = 0; i < (impl.Deps ?? []).Count; i++)
            depsWithIndex.Add(((impl.Deps ?? [])[i], i));
        depsWithIndex.AddRange(IndexedScenarioDeps(impl));

        foreach (var (dep, index) in depsWithIndex)
        {
            var fake = dep.Fake;
            var hasFakeSpecific = dep.Contract is not null || dep.Wrap is not null
                || (dep.Methods?.Count > 0) || (dep.Values?.Count > 0);

            if (string.IsNullOrWhiteSpace(fake))
            {
                if (hasFakeSpecific)
                    violations.Add($"C12 端口[{port.Key}] 的 deps[{index}] 未给 fake 却用了 contract/wrap/methods/values（仅 fake 形态可带）");
            }
            else if (dep.Contract is null && dep.Wrap is null)
            {
                violations.Add($"C12 端口[{port.Key}] 的 deps[{index}].fake='{fake}' 须给 contract 或 wrap 至少一个");
            }
        }
    }

    // ---------------------------------------------------------------- C13 / C14

    /// <summary>C13（投影对账）：<c>project[].field ⊆ ports[key].out.fields</c>（Ordinal；越界键即错）；
    /// 脚本 <c>scriptProject</c> 同理；<c>impl.productFields</c>（<c>|</c> 分隔）⊆ 契约字段。
    /// <para>弱校验（口径唯一 = <see cref="OutFieldContract"/>）：<c>out.fields</c> 为空 ⇒ 一律不判（表形态/数据驱动键面）。</para>
    /// </summary>
    internal static void CheckC13Projection(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null) return;

        var outFields = port.OutFields;
        if (outFields.Count == 0) return; // 弱校验：契约未声明字段

        foreach (var field in OutFieldContract.Excess(outFields, (impl.Project ?? []).Select(item => item.Field)))
            violations.Add($"C13 端口[{port.Key}] 的 project 产出字段 '{field}' 未在 out.fields 声明");

        foreach (var field in OutFieldContract.Excess(outFields, (impl.ScriptProject ?? []).Select(item => item.Field)))
            violations.Add($"C13 端口[{port.Key}] 的 scriptProject 产出字段 '{field}' 未在 out.fields 声明");

        foreach (var field in OutFieldContract.Excess(outFields, SplitProductFields(impl.ProductFields)))
            violations.Add($"C13 端口[{port.Key}] 的 productFields 含字段 '{field}' 未在 out.fields 声明");
    }

    /// <summary>
    /// C14（断言面覆盖，D8-a 的装载期判据）：<c>ports[key].out.fields ⊆ impl.productFields</c>（Ordinal）——
    /// 契约输出面的每个字段都必须在**断言面**（<c>then.path</c> 首段取值域）有声明，否则用例无从断言。
    /// <para>与 C13 互为对偶（C13 管「产出不越界」，本判据管「契约不悬空」），二者与
    /// <see cref="OutFieldContract"/> 同源：**双源保留、对拍判据单源**（D8-a 裁决）。</para>
    /// <para>弱校验：<c>impl.productFields</c> 为空 ⇒ 一律不判（口径同 C13 与内核 FieldContractValidator）。</para>
    /// </summary>
    internal static void CheckC14ProductFieldCoverage(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null) return;

        var productFields = SplitProductFields(impl.ProductFields).ToArray();
        if (productFields.Length == 0) return; // 弱校验：断言面未声明

        foreach (var field in OutFieldContract.Excess(productFields, port.OutFields))
            violations.Add($"C14 端口[{port.Key}] 的 out.fields 契约字段 '{field}' 未进 impl.productFields（断言面缺声明）");
    }

    // ---------------------------------------------------------------- 递归下钻

    /// <summary>递归访问 impl 下全部 <see cref="ValueSourceStandard"/>（含场景主体），供 C8/C11。</summary>
    private static void WalkValueSources(PortImplStandard impl, Action<ValueSourceStandard> visit)
    {
        foreach (var bind in (impl.Binds ?? new Dictionary<string, ValueSourceStandard>()).Values) WalkValueSource(bind, visit);
        foreach (var dep in impl.Deps ?? []) foreach (var arg in dep.Args ?? []) WalkValueSource(AsValueSource(arg), visit);
        foreach (var call in impl.Calls ?? []) foreach (var arg in call.Args ?? []) WalkValueSource(AsValueSource(arg), visit);
        foreach (var step in impl.Script ?? []) WalkStep(step, visit);
        foreach (var scope in impl.Scopes ?? []) WalkScope(scope, visit);
        if (impl.Deliver is { } deliver) WalkDeliver(deliver, visit);
        foreach (var scenario in (impl.Scenarios?.Cases ?? new Dictionary<string, ScenarioBodyStandard>()).Values)
            WalkScenario(scenario, visit);
    }

    /// <summary>递归访问全部脚本步骤（含场景/折叠/委托体内），供 C10。</summary>
    private static void WalkSteps(PortImplStandard impl, Action<StepStandard> visit)
    {
        foreach (var step in impl.Script ?? []) WalkStep(step, visit);
        foreach (var scope in impl.Scopes ?? [])
        {
            foreach (var open in scope.Open ?? []) WalkStep(open, visit);
            foreach (var close in scope.Close ?? []) WalkStep(close, visit);
        }
        if (impl.Deliver is { } deliver)
        {
            foreach (var deferred in deliver.DeferSteps ?? []) WalkStep(deferred, visit);
            // capture 是载体元声明（含 Call 但不属主脚本步骤），step 遍历不进入
        }
        foreach (var scenario in (impl.Scenarios?.Cases ?? new Dictionary<string, ScenarioBodyStandard>()).Values)
            WalkScenarioSteps(scenario, visit);
    }

    /// <summary>递归访问全部 foreach 步骤，供 C10。</summary>
    private static void WalkForeaches(PortImplStandard impl, Action<ForeachStandard> visit)
    {
        WalkSteps(impl, step =>
        {
            if (step.Foreach is { } foreachStep) visit(foreachStep);
        });
    }

    /// <summary>步骤递归（step 访问版）：下钻嵌套步骤（foreach.do / 委托体内 / 场景 / 裁剪），对每个步骤回调。</summary>
    private static void WalkStep(StepStandard step, Action<StepStandard> visit)
    {
        visit(step);
        if (step.Foreach is { } foreachStep)
            foreach (var body in foreachStep.Do ?? []) WalkStep(body, visit);
    }

    /// <summary>步骤递归（值源访问版）：下钻全部取值来源（含嵌套步骤体内、委实体、场景）。</summary>
    private static void WalkStep(StepStandard step, Action<ValueSourceStandard> visit)
    {
        if (step.When is { } when) WalkExpr(when, visit);
        if (step.New is { } newStep) WalkNew(newStep, visit);
        if (step.Invoke is { } invoke) WalkInvoke(invoke, visit);
        if (step.Foreach is { } foreachStep)
        {
            if (foreachStep.Items is { } items) WalkExpr(items, visit);
            if (foreachStep.Where is { } where) WalkExpr(where, visit);
            foreach (var body in foreachStep.Do ?? []) WalkStep(body, visit);
        }
    }

    private static void WalkNew(NewStandard newStep, Action<ValueSourceStandard> visit)
    {
        foreach (var arg in newStep.Args ?? []) WalkValueSource(arg, visit);
        foreach (var chain in newStep.Chain ?? []) foreach (var arg in chain.Args ?? []) WalkValueSource(arg, visit);
        foreach (var init in newStep.Init ?? []) if (init.Value is { } value) WalkValueSource(value, visit);
    }

    private static void WalkInvoke(InvokeStandard invoke, Action<ValueSourceStandard> visit)
    {
        foreach (var arg in invoke.Args ?? []) WalkValueSource(arg, visit);
        if (invoke.Value is { } value) WalkValueSource(value, visit);
    }

    private static void WalkScope(ScopeStandard scope, Action<ValueSourceStandard> visit)
    {
        if (scope.Invoke is { } invoke) WalkInvoke(invoke, visit);
        foreach (var file in scope.Files ?? [])
        {
            if (file.From is { } from) WalkValueSource(from, visit);
            if (file.Content is { } content) WalkExpr(content, visit);
        }
        foreach (var open in scope.Open ?? []) WalkStep(open, visit);
        foreach (var close in scope.Close ?? []) WalkStep(close, visit);
    }

    private static void WalkDeliver(DeliverStandard deliver, Action<ValueSourceStandard> visit)
    {
        if (deliver.Value is { } value) WalkExpr(value, visit);
        foreach (var deferred in deliver.DeferSteps ?? []) WalkStep(deferred, visit);
        if (deliver.Capture is { } capture)
        {
            if (capture.Call is { } call) WalkInvoke(call, visit);
            if (capture.When is { } when) WalkExpr(when, visit);
        }
    }

    private static void WalkScenario(ScenarioBodyStandard body, Action<ValueSourceStandard> visit)
    {
        foreach (var bind in (body.Binds ?? new Dictionary<string, ValueSourceStandard>()).Values) WalkValueSource(bind, visit);
        foreach (var dep in body.Deps ?? []) foreach (var arg in dep.Args ?? []) WalkValueSource(AsValueSource(arg), visit);
        foreach (var step in body.Script ?? []) WalkStep(step, visit);
        foreach (var scope in body.Scopes ?? []) WalkScope(scope, visit);
        if (body.Deliver is { } deliver) WalkDeliver(deliver, visit);
    }

    /// <summary>场景主体内的步骤（step 访问版），供 C10。</summary>
    private static void WalkScenarioSteps(ScenarioBodyStandard body, Action<StepStandard> visit)
    {
        foreach (var step in body.Script ?? []) WalkStep(step, visit);
        foreach (var scope in body.Scopes ?? [])
        {
            foreach (var open in scope.Open ?? []) WalkStep(open, visit);
            foreach (var close in scope.Close ?? []) WalkStep(close, visit);
        }
        if (body.Deliver is { } deliver)
        {
            foreach (var deferred in deliver.DeferSteps ?? []) WalkStep(deferred, visit);
        }
    }

    private static void WalkExpr(ExprStandard expr, Action<ValueSourceStandard> visit)
    {
        if (expr.Source is { } source) WalkValueSource(source, visit);
        Walk(expr.Count, visit); Walk(expr.NotNull, visit); Walk(expr.AsText, visit); Walk(expr.TypeName, visit);
        Walk(expr.Message, visit); Walk(expr.Thrown, visit); Walk(expr.Contains, visit); Walk(expr.Of, visit);
        Walk(expr.All, visit); Walk(expr.Any, visit); Walk(expr.AllIn, visit); Walk(expr.Spread, visit);
        Walk(expr.IsNull, visit); Walk(expr.Not, visit); Walk(expr.First, visit); Walk(expr.FirstWhere, visit);
        Walk(expr.Agg, visit); Walk(expr.Filter, visit); Walk(expr.FilterWhere, visit); Walk(expr.Dict, visit);
        Walk(expr.DictKey, visit); Walk(expr.DictValue, visit); Walk(expr.DictWhere, visit); Walk(expr.Last, visit);
        Walk(expr.LastWhere, visit); Walk(expr.Distinct, visit);
        foreach (var e in expr.Concat ?? []) Walk(e, visit);
        foreach (var e in expr.CountEqual ?? []) Walk(e, visit);
        foreach (var e in expr.Eq ?? []) Walk(e, visit);
        foreach (var e in expr.Ne ?? []) Walk(e, visit);
        foreach (var e in expr.Gt ?? []) Walk(e, visit);
        foreach (var e in expr.Ge ?? []) Walk(e, visit);
        foreach (var e in expr.Lt ?? []) Walk(e, visit);
        foreach (var e in expr.Le ?? []) Walk(e, visit);
        foreach (var e in expr.AllOf ?? []) Walk(e, visit);
        foreach (var e in expr.AnyOf ?? []) Walk(e, visit);
        foreach (var e in expr.SameReference ?? []) Walk(e, visit);
        foreach (var e in expr.Coalesce ?? []) Walk(e, visit);
        foreach (var e in expr.SequenceEqual ?? []) Walk(e, visit);
        foreach (var part in expr.Parts ?? []) { Walk(part.Expr, visit); if (part.When is { } when) Walk(when, visit); }
        if (expr.Invoke is { } invoke) WalkInvoke(invoke, visit);
        if (expr.FoldInit is { } foldInit) WalkValueSource(foldInit, visit);
        if (expr.FoldStep is { } foldStep) foreach (var arg in foldStep.Args ?? []) WalkValueSource(arg, visit);

        void Walk(ExprStandard? e, Action<ValueSourceStandard> v)
        {
            if (e is not null) WalkExpr(e, v);
        }
    }

    private static void WalkValueSource(ValueSourceStandard source, Action<ValueSourceStandard> visit)
    {
        visit(source);
        if (source.Rows is { } rows) WalkValueSource(rows, visit);
        if (source.Items is { } items) WalkValueSource(items, visit);
        if (source.RowValue is { } rowValue) WalkExpr(rowValue, visit);
        if (source.Filter is { } filter) WalkExpr(filter, visit);
        if (source.Construct is { } construct) WalkNew(construct, visit);
        if (source.Delegate is { } delegateSpec) foreach (var step in delegateSpec.Steps ?? []) WalkStep(step, visit);
    }

    /// <summary>组合族实参暂时以取值来源形态参与遍历（只取 C8 关心的 <c>Dep</c> 伴生字段——组合实参无 <c>from</c>，故实际不触发 C8）。</summary>
    private static ValueSourceStandard AsValueSource(ComposedArgStandard arg)
        => new(Dep: arg.Dep, Index: arg.ArgIndex, Key: arg.Key);

    /// <summary>汇总端口级 + 场景级作用域名（<c>scopes[].as</c>）。</summary>
    private static void CollectScopeNames(PortImplStandard impl, HashSet<string> scopes)
    {
        foreach (var scope in impl.Scopes ?? []) if (!string.IsNullOrWhiteSpace(scope.As)) scopes.Add(scope.As);
        foreach (var scenario in (impl.Scenarios?.Cases ?? new Dictionary<string, ScenarioBodyStandard>()).Values)
            foreach (var scope in scenario.Scopes ?? []) if (!string.IsNullOrWhiteSpace(scope.As)) scopes.Add(scope.As);
    }

    /// <summary>枚举场景主体内的依赖（附带其所在端口 <c>deps</c> 数组全局下标，仅用于 C12 诊断定位）。</summary>
    private static IEnumerable<(DepStandard Dep, int Index)> IndexedScenarioDeps(PortImplStandard impl)
    {
        var offset = (impl.Deps ?? []).Count;
        var index = -1;
        foreach (var scenario in (impl.Scenarios?.Cases ?? new Dictionary<string, ScenarioBodyStandard>()).Values)
            foreach (var dep in scenario.Deps ?? []) yield return (dep, offset + ++index);
    }

    /// <summary><c>productFields</c>（<c>|</c> 分隔）切分并去空白。</summary>
    private static IEnumerable<string> SplitProductFields(string productFields)
    {
        foreach (var piece in productFields.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return piece;
    }
}
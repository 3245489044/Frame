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
        if (impl is null || string.IsNullOrWhiteSpace(impl.Kind) || string.Equals(impl.Kind, PortImplVocabulary.KindName.Delegate, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(impl?.Id))
                violations.Add($"C7 端口[{port.Key}] 的 impl 缺实现（kind=delegate 但未给 id）——疑忘写实现");
            return;
        }

        if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Reflective, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(impl.Target) || string.IsNullOrWhiteSpace(impl.Method))
                violations.Add($"C7 端口[{port.Key}] 的 impl.kind=reflective 须给 target 与 method（当前 target='{impl.Target}' method='{impl.Method}'）");
            return;
        }

        if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Frozen, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(impl.Reason))
            violations.Add($"C7 端口[{port.Key}] 的 impl.kind=frozen 须给 reason（冻结须说明原因）");
    }

    // ---------------------------------------------------------------- C8

    /// <summary>C8：取值来源 <c>from</c> ↔ 伴生键 一致性（校对 #7：仅 <c>arg</c> 须带 <c>index</c>；<c>item</c> 配 <c>key</c>+<c>path</c>，不强制 index）。
    /// <para>枚举成员（<c>from</c>/<c>convert</c>）由结构门守，此处只判「选了某来源却缺其必随键」的条件关系。</para>
    /// </summary>
    internal static void CheckC8ValueSources(PortStandard port, PortImplStandard? impl, List<string> violations)
    {
        if (impl is null) return;

        StandardNodeWalk.VisitValueSources(impl, source =>
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
        if (impl is null || !string.Equals(impl.Kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal)) return;

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
        if (impl is null || !string.Equals(impl.Kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal)) return;

        StandardNodeWalk.VisitSteps(impl, step =>
        {
            var forms = 0;
            if (step.New is not null) forms++;
            if (step.Invoke is not null) forms++;
            if (step.Foreach is not null) forms++;

            if (forms > 1)
                violations.Add($"C10 端口[{port.Key}] 的脚本步骤形态不止一个（new/invoke/foreach 出现了 {forms} 个），须恰一");

            if (step.Foreach is { } foreachStep && (foreachStep.Do is null || foreachStep.Do.Count == 0))
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
        if (impl is null || !string.Equals(impl.Kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal)) return;

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

        StandardNodeWalk.VisitValueSources(impl, source =>
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

    // 唯一「父→子」规则与遍历入口 = StandardNodeWalk（契约层子槽表 + 零依赖遍历骨架）；本件只留判据体。

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
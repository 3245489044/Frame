using System.Text.Json;
using DataCenter.Protocol.Standards;
using DataCenter.Protocol.Standards.Descriptors;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>一条干跑校验问题（<c>Severity</c> ∈ <c>error</c> / <c>warn</c>）。</summary>
internal sealed record LintIssue(string PortKey, string Rule, string Severity, string Path, string Message);

/// <summary>
/// <c>lint</c>：**声明体干跑校验**（不构建、不装配、不反射）—— 直接在分片真源上按标准层词表与
/// <see cref="LintPolicy"/> 政策报「键位置 / 算子可达 / 槽拼写」三类问题，把反馈回路从一次构建压到一次进程启动。
/// <para>
/// 分工（避免第二判据源）：本命令是**作者面快反馈快照**，判据口径逐条对齐构建期权威门
/// <c>ScriptedDeclarationGuardTests</c>（槽 / 遍历变量 / 依赖下标 / 收集槽）与标准层词表（键闭包 / 取值域）；
/// **权威仍是构建期门与守卫**，本命令不作放行依据。守卫专属项（<c>expect</c> / <c>delegate.kind</c> 类型可解析、
/// 装配态 <c>Unbound</c> 记账）不在本命令范围（需产品程序集反射）。
/// </para>
/// <para>政策单源 = 数据视图 <c>lint</c> 段（严重度 / 平铺组 / 不可达算子 / 文案）；本件零端口键、零领域词。</para>
/// </summary>
internal static class LintCommand
{
    internal static int Run(LedgerView view, string? only)
    {
        var policy = LintPolicy.Load(view.DataPath);
        var issues = new List<LintIssue>();
        var keys = 0;

        if (!Directory.Exists(view.PartsDirectory))
        {
            Console.WriteLine(string.Format(view.Message("notSplitYet"), view.PartsDirectory));
            return 0;
        }

        foreach (var group in view.Groups)
        {
            var shardPath = Path.Combine(view.PartsDirectory, group.File);
            if (!File.Exists(shardPath))
                throw new InvalidOperationException(string.Format(view.Message("missingShard"), shardPath, group.Name));

            using var document = JsonDocument.Parse(File.ReadAllText(shardPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, view.ShardCommentKey, StringComparison.Ordinal)) continue;
                if (only is { Length: > 0 } filter && !property.Name.StartsWith(filter, StringComparison.Ordinal)) continue;

                keys++;
                CheckPort(policy, property.Name, property.Value, issues);
            }
        }

        return Report(view, policy, issues, keys, view.Groups.Count);
    }

    /// <summary>逐键校验：键闭包 → 词表取值 → 分支（scripted / composed / reflective / frozen）→ 槽拼写。</summary>
    private static void CheckPort(LintPolicy policy, string portKey, JsonElement port, List<LintIssue> issues)
    {
        var sink = new Sink(policy, issues, portKey);

        if (port.ValueKind != JsonValueKind.Object
            || !port.TryGetProperty("impl", out var impl)
            || impl.ValueKind != JsonValueKind.Object)
        {
            sink.Add("impl-missing", portKey);
            return;
        }

        var inKeys = Members(port, "in").Select(member => member.Name).ToArray();
        var kind = String(impl, "kind") ?? string.Empty;
        sink.DepCount = Elements(impl, "deps").Count;

        sink.Keys("impl", impl, StandardDescriptors.Impl.Keys);
        sink.Word("impl.kind", String(impl, "kind"), PortImplVocabulary.Kinds);
        if (string.Equals(kind, "delegate", StringComparison.Ordinal)) sink.Add("legacy-kind", "impl.kind");
        if (Has(impl, "legacyKind")) sink.Add("legacy-kind-field", "impl.legacyKind");

        CheckDeps(sink, impl, "impl.deps");
        CheckBinds(sink, impl, inKeys);
        CheckScopes(sink, impl);

        switch (kind)
        {
            case "composed":
                CheckComposed(sink, impl);
                break;

            case "reflective":
                CheckReflective(sink, impl);
                break;

            case "frozen":
                if (String(impl, "reason") is not { Length: > 0 }) sink.Add("frozen-reason-missing", "impl.reason");
                break;

            default:
                CheckScripted(sink, impl, inKeys);
                break;
        }
    }

    /// <summary>反射族：<c>target</c> / <c>method</c> 齐备 + 实参声明键闭包与取值域。</summary>
    private static void CheckReflective(Sink sink, JsonElement impl)
    {
        if (String(impl, "target") is not { Length: > 0 } || String(impl, "method") is not { Length: > 0 })
            sink.Add("reflective-target-missing", "impl.target");

        var index = 0;
        foreach (var arg in Elements(impl, "args"))
        {
            var path = $"impl.args[{index++}]";
            sink.Keys(path, arg, StandardDescriptors.Arg.Keys);
            sink.Word($"{path}.convert", String(arg, "convert"), PortImplVocabulary.Conversions);
            sink.Word($"{path}.from", String(arg, "from"), PortImplVocabulary.ValueSourceFromAll);
            CheckValueSource(sink, path, arg, valueSource: false);
        }
    }

    /// <summary>组合族：<c>calls</c> / <c>project</c> / <c>deps[].fake</c> 键闭包与取值域。</summary>
    private static void CheckComposed(Sink sink, JsonElement impl)
    {
        var index = 0;
        foreach (var call in Elements(impl, "calls"))
        {
            var path = $"impl.calls[{index++}]";
            sink.Keys(path, call, StandardDescriptors.Call.Keys);
            CheckArgArray(sink, $"{path}.args", Elements(call, "args"), StandardDescriptors.ComposedArg.Keys,
                valueSource: false);
        }

        var fieldIndex = 0;
        foreach (var field in Elements(impl, "project"))
        {
            var path = $"impl.project[{fieldIndex++}]";
            sink.Keys(path, field, StandardDescriptors.ComposedField.Keys);
            sink.Word($"{path}.select", String(field, "select"), PortImplVocabulary.Selects);
        }
    }


    // ── 依赖 / 绑定 / 作用域 ──────────────────────────────────────────

    private static void CheckDeps(Sink sink, JsonElement impl, string path)
    {
        var index = 0;
        foreach (var dep in Elements(impl, "deps"))
        {
            var item = $"{path}[{index++}]";
            sink.Keys(item, dep, StandardDescriptors.Dep.Keys);
            CheckArgArray(sink, $"{item}.args", Elements(dep, "args"), StandardDescriptors.ComposedArg.Keys,
                valueSource: false);

            if (dep.TryGetProperty("fake", out var fake) && fake.ValueKind == JsonValueKind.Object)
                sink.Word($"{item}.fake.kind", String(fake, "kind"), PortImplVocabulary.FakeKinds);
        }
    }

    /// <summary>输入绑定：键闭包 + 槽位在端口 <c>in</c> 面登记（提示级）+ <c>with</c> 来源键核对。</summary>
    private static void CheckBinds(Sink sink, JsonElement impl, IReadOnlyList<string> inKeys)
    {
        if (!impl.TryGetProperty("bind", out var bind) || bind.ValueKind != JsonValueKind.Object) return;

        foreach (var slot in bind.EnumerateObject())
        {
            var path = $"impl.bind['{slot.Name}']";
            CheckValueSource(sink, path, slot.Value);
            CheckWithKey(sink, path, slot.Value, inKeys);

            if (!inKeys.Contains(slot.Name, StringComparer.Ordinal))
                sink.Add("bind-slot-undeclared", path, slot.Name, string.Join("|", inKeys));
        }
    }

    /// <summary>作用域：键闭包 + <c>kind</c> 取值域 + 预置文件 + 进入期留存槽。</summary>
    private static void CheckScopes(Sink sink, JsonElement impl)
    {
        var index = 0;
        foreach (var scope in Elements(impl, "scopes"))
        {
            var path = $"impl.scopes[{index++}]";
            sink.Keys(path, scope, StandardDescriptors.Scope.Keys);
            sink.Word($"{path}.kind", String(scope, "kind"), PortImplVocabulary.ScopeKinds);

            var saveIndex = 0;
            foreach (var item in Elements(scope, "save"))
            {
                var savePath = $"{path}.save[{saveIndex++}]";
                sink.Keys(savePath, item, StandardDescriptors.Save.Keys);
                if (String(item, "slot") is not { Length: > 0 }) sink.Add("save-slot-missing", $"{savePath}.slot");
            }

            var fileIndex = 0;
            foreach (var file in Elements(scope, "files"))
            {
                var filePath = $"{path}.files[{fileIndex++}]";
                sink.Keys(filePath, file, StandardDescriptors.File.Keys);
                if (file.TryGetProperty("from", out var source)) CheckValueSource(sink, $"{filePath}.from", source);
                if (file.TryGetProperty("when", out var when)) WalkExpr(sink, $"{filePath}.when", when);
            }
        }
    }


    // ── scripted：声明体 / 槽拼写 / 算子可达 ─────────────────────────────────

    /// <summary>
    /// 脚本声明校验（口径对齐 <c>ScriptedDeclarationGuardTests.DeclarationProblems</c>）：
    /// ① 收集槽预建（执行前已建空清单）⇒ 与 bind 槽同属「已定义槽」；② 步骤按声明序推进槽集；
    /// ③ 遍历体内可见循环变量；④ 投影字段 ≡ productFields（同集，去序）。
    /// </summary>
    private static void CheckScripted(Sink sink, JsonElement impl, IReadOnlyList<string> inKeys)
    {
        var slots = Members(impl, "bind").Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var collectSlots = new HashSet<string>(StringComparer.Ordinal);
        GatherCollects(impl, collectSlots);
        foreach (var slot in collectSlots) slots.Add(slot);

        var loops = new List<string>();
        var context = Has(impl, "scenarios") ? "scenario" : null;
        sink.Collects = collectSlots;

        if (impl.TryGetProperty("script", out var script) && script.ValueKind == JsonValueKind.Array)
            WalkSteps(sink, script, slots, loops, collectSlots, context, "impl.script");

        if (impl.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var field in project.EnumerateArray())
            {
                var path = $"impl.project[{index++}]";
                sink.Keys(path, field, ProjectFieldItemKeys);
                if (field.TryGetProperty("expr", out var expr))
                    WalkExpr(sink, $"{path}.expr", expr, slots, loops, context);
            }

            CheckProjectFields(sink, impl, project);
        }

        CheckDeliver(sink, impl, slots, loops, collectSlots, context);
        CheckScenarios(sink, impl, inKeys);
        CheckBehaviours(sink, impl);
    }

    /// <summary>投影字段 ≡ <c>impl.productFields</c>（同集，去序；<c>spread</c> 项不占字段位）。</summary>
    private static void CheckProjectFields(Sink sink, JsonElement impl, JsonElement project)
    {
        var declared = (String(impl, "productFields") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var projected = project.EnumerateArray()
            .Where(field => !Has(field, "spread"))
            .Select(field => String(field, "field"))
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)
            .ToArray();

        if (declared.Length == 0 && projected.Length == 0) return;
        if (declared.ToHashSet(StringComparer.Ordinal).SetEquals(projected)) return;

        sink.Add("project-field-drift", "impl.project", string.Join(",", projected), string.Join(",", declared));
    }

    /// <summary>载体（<c>deliver</c>）校验：键闭包 + <c>kind</c> 词表 + 延迟步骤 + issue 捕获体。</summary>
    private static void CheckDeliver(
        Sink sink, JsonElement impl, HashSet<string> slots, List<string> loops, HashSet<string> collects,
        string? context)
    {
        if (!impl.TryGetProperty("deliver", out var deliver) || deliver.ValueKind != JsonValueKind.Object) return;

        sink.Collects = collects;
        sink.Keys("impl.deliver", deliver, StandardDescriptors.Deliver.Keys);
        sink.Word("impl.deliver.kind", String(deliver, "kind"), PortImplVocabulary.DeliverKinds);

        if (deliver.TryGetProperty("value", out var value))
            WalkExpr(sink, "impl.deliver.value", value, slots, loops, context);

        if (deliver.TryGetProperty("defer", out var deferred))
        {
            if (deferred.ValueKind == JsonValueKind.Array)
                WalkSteps(sink, deferred, slots, loops, collects, context, "impl.deliver.defer");

            if (deferred.ValueKind == JsonValueKind.Object
                && deferred.TryGetProperty("steps", out var deferredSteps)
                && deferredSteps.ValueKind == JsonValueKind.Array)
                WalkSteps(sink, deferredSteps, slots, loops, collects, context, "impl.deliver.defer.steps");
        }

        if (!deliver.TryGetProperty("capture", out var capture) || capture.ValueKind != JsonValueKind.Object) return;

        sink.Keys("impl.deliver.capture", capture, StandardDescriptors.Capture.Keys);
        sink.Word("impl.deliver.capture.message", String(capture, "message"), PortImplVocabulary.MessageModes);
        CheckArgArray(sink, "impl.deliver.capture.args", Elements(capture, "args"),
            StandardDescriptors.ValueSource.Keys, slots, loops, context);

        if (capture.TryGetProperty("when", out var when))
            WalkExpr(sink, "impl.deliver.capture.when", when, slots, loops, context);

        if (String(capture, "messageField") is { Length: > 0 } field && !slots.Contains(field))
            sink.Add("slot-undefined", "impl.deliver.capture.messageField", field, context: context);
    }


    /// <summary>场景体校验（每场景独立作用域：自带 <c>bind</c>；D-3 未决 ⇒ 未定义槽按 <c>@scenario</c> 取严重度）。</summary>
    private static void CheckScenarios(Sink sink, JsonElement impl, IReadOnlyList<string> inKeys)
    {
        if (!impl.TryGetProperty("scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Object) return;

        sink.Keys("impl.scenarios", scenarios, StandardDescriptors.Scenario.Keys);
        if (!Has(scenarios, "select"))
            sink.Add("scenario-select-missing", "impl.scenarios.select");

        if (!scenarios.TryGetProperty("cases", out var cases) || cases.ValueKind != JsonValueKind.Object) return;

        if (!cases.EnumerateObject().Any())
        {
            sink.Add("scenario-empty", "impl.scenarios.cases");
            return;
        }

        foreach (var scenario in cases.EnumerateObject())
        {
            if (scenario.Name.Length == 0)
            {
                sink.Add("scenario-empty-name", "impl.scenarios.cases");
                continue;
            }

            var body = scenario.Value;
            var path = $"impl.scenarios.cases['{scenario.Name}']";
            var slots = Members(body, "bind").Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
            var collects = new HashSet<string>(StringComparer.Ordinal);
            GatherCollects(body, collects);
            foreach (var slot in collects) slots.Add(slot);

            var loops = new List<string>();
            sink.Collects = collects;

            if (body.TryGetProperty("script", out var script) && script.ValueKind == JsonValueKind.Array)
                WalkSteps(sink, script, slots, loops, collects, "scenario", $"{path}.script");

            var index = 0;
            foreach (var field in Elements(body, "project"))
            {
                var fieldPath = $"{path}.project[{index++}]";
                sink.Keys(fieldPath, field, ProjectFieldItemKeys);
                if (field.TryGetProperty("expr", out var expr))
                    WalkExpr(sink, $"{fieldPath}.expr", expr, slots, loops, "scenario");
            }

            CheckDeliver(sink, body, slots, loops, collects, "scenario");
            CheckDeps(sink, body, $"{path}.deps");

            foreach (var slot in Members(body, "bind"))
            {
                var bindPath = $"{path}.bind['{slot.Name}']";
                CheckValueSource(sink, bindPath, slot.Value, "scenario");
                CheckWithKey(sink, bindPath, slot.Value, inKeys);
            }
        }
    }

    /// <summary>无行为登记（对齐 <c>FD-01</c> 棘轮口径；提示级，指向棘轮清单）。</summary>
    private static void CheckBehaviours(Sink sink, JsonElement impl)
    {
        // 多场景条目走场景面判据（守卫同分叉）。
        if (Has(impl, "scenarios")) return;

        var hasScript = impl.TryGetProperty("script", out var script)
                        && script.ValueKind == JsonValueKind.Array && script.GetArrayLength() > 0;
        var hasProject = impl.TryGetProperty("project", out var project)
                         && project.ValueKind == JsonValueKind.Array && project.GetArrayLength() > 0;

        var deliver = impl.TryGetProperty("deliver", out var carrier) ? carrier : default;
        var hasValue = deliver.ValueKind == JsonValueKind.Object
                       && deliver.TryGetProperty("value", out var value)
                       && value.ValueKind == JsonValueKind.Object;
        var hasCapture = deliver.ValueKind == JsonValueKind.Object
                         && deliver.TryGetProperty("capture", out var capture)
                         && capture.ValueKind == JsonValueKind.Object
                         && capture.EnumerateObject().Any();
        var hasDefer = deliver.ValueKind == JsonValueKind.Object
                       && deliver.TryGetProperty("defer", out var deferred)
                       && (deferred.ValueKind == JsonValueKind.Object
                           || (deferred.ValueKind == JsonValueKind.Array && deferred.GetArrayLength() > 0)
                           || deferred.ValueKind == JsonValueKind.True);

        if (hasScript || hasProject || hasValue || hasCapture || hasDefer) return;
        sink.Add("no-behaviour", "impl");
    }


    // ── 步骤遍历 ────────────────────────────────────────────────────────────

    /// <summary>步骤遍历（按声明序推进槽集；遍历体内可见循环变量）—— 口径对齐守卫 <c>WalkSteps</c>。</summary>
    private static void WalkSteps(
        Sink sink, JsonElement steps, HashSet<string> slots, List<string> loops, HashSet<string> collects,
        string? context, string path)
    {
        var index = 0;
        foreach (var step in steps.EnumerateArray())
        {
            var item = $"{path}[{index++}]";
            sink.Keys(item, step, StandardDescriptors.Step.Keys);

            if (step.TryGetProperty("when", out var when))
                WalkExpr(sink, $"{item}.when", when, slots, loops, context);

            if (step.TryGetProperty("new", out var construct) && construct.ValueKind == JsonValueKind.Object)
            {
                CheckConstruct(sink, $"{item}.new", construct, slots, loops, context);
                if (String(construct, "as") is { Length: > 0 } asName) slots.Add(asName);
                CheckCollects(sink, step, slots, loops, context, item);
                continue;
            }

            if (step.TryGetProperty("invoke", out var invoke) && invoke.ValueKind == JsonValueKind.Object)
            {
                sink.Keys($"{item}.invoke", invoke, StandardDescriptors.Invoke.Keys);
                CheckArgArray(sink, $"{item}.invoke.args", Elements(invoke, "args"),
                    StandardDescriptors.ValueSource.Keys, slots, loops, context);

                if (String(invoke, "as") is { Length: > 0 } asName) slots.Add(asName);
                if (String(invoke, "out") is { Length: > 0 } outName) slots.Add(outName);

                CheckCollects(sink, step, slots, loops, context, item);
                continue;
            }

            if (step.TryGetProperty("foreach", out var collection) || step.TryGetProperty("items", out _))
            {
                var slotName = String(step, "as") ?? string.Empty;

                if (step.TryGetProperty("items", out var items))
                    WalkExpr(sink, $"{item}.foreach.items", items, slots, loops, context);
                if (step.TryGetProperty("where", out var where))
                    WalkExpr(sink, $"{item}.foreach.where", where, slots, loops, context);
                CheckCollects(sink, step, slots, loops, context, item);

                if (collection.ValueKind == JsonValueKind.String
                    && collection.GetString() is { Length: > 0 } source
                    && !slots.Contains(source))
                    sink.Add("foreach-slot-undefined", item, source, context: context);

                if (slotName.Length > 0) loops.Add(slotName);
                if (step.TryGetProperty("do", out var body) && body.ValueKind == JsonValueKind.Array)
                    WalkSteps(sink, body, slots, loops, collects, context, $"{item}.foreach.do");
                if (slotName.Length > 0) loops.RemoveAt(loops.Count - 1);

                continue;
            }

            sink.Add("step-shape-missing", item);
        }
    }

    /// <summary>构造步骤（<c>new</c>）：键闭包 + 初始化项 + 链式调用。</summary>
    private static void CheckConstruct(
        Sink sink, string path, JsonElement construct, HashSet<string>? slots, List<string>? loops, string? context)
    {
        sink.Keys(path, construct, StandardDescriptors.New.Keys);

        if (construct.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            CheckArgArray(sink, $"{path}.args", args.EnumerateArray().ToArray(), null, slots, loops, context,
                valueSource: false);

        var index = 0;
        foreach (var link in Elements(construct, "chain"))
        {
            var linkPath = $"{path}.chain[{index++}]";
            sink.Keys(linkPath, link, StandardDescriptors.Chain.Keys);
            CheckArgArray(sink, $"{linkPath}.args", Elements(link, "args"), null, slots, loops, context,
                valueSource: false);
        }

        var initIndex = 0;
        foreach (var member in Elements(construct, "init"))
        {
            var memberPath = $"{path}.init[{initIndex++}]";
            sink.Keys(memberPath, member, StandardDescriptors.Init.Keys);
            if (member.TryGetProperty("value", out var value))
                CheckValueSource(sink, $"{memberPath}.value", value, context, slots, loops);
        }
    }


    /// <summary>收集声明校验：<c>collect.from</c> 非空 = 槽源（须先写入该槽或指向本步骤产物）。</summary>
    private static void CheckCollects(
        Sink sink, JsonElement step, HashSet<string> slots, List<string> loops, string? context, string path)
    {
        if (!step.TryGetProperty("collect", out var collects) || collects.ValueKind != JsonValueKind.Array) return;

        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shape in new[] { "new", "invoke" })
        {
            if (!step.TryGetProperty(shape, out var node) || node.ValueKind != JsonValueKind.Object) continue;
            if (String(node, "as") is { Length: > 0 } asName) produced.Add(asName);
            if (String(node, "out") is { Length: > 0 } outName) produced.Add(outName);
        }

        var index = 0;
        foreach (var collect in collects.EnumerateArray())
        {
            var item = $"{path}.collect[{index++}]";
            sink.Keys(item, collect, StandardDescriptors.Collect.Keys);

            if (String(collect, "from") is not { Length: > 0 } source) continue;
            if (slots.Contains(source) || produced.Contains(source) || loops.Contains(source)) continue;

            sink.Add("collect-source-undefined", item, source, context: context);
        }
    }

    /// <summary>收集槽名集（步骤 / 遍历 / 延迟步骤声明；收集槽预建 ⇒ 与 bind 槽同属已定义槽）。</summary>
    private static void GatherCollects(JsonElement owner, HashSet<string> collects)
    {
        foreach (var name in new[] { "script", "defer" })
            if (owner.TryGetProperty(name, out var steps) && steps.ValueKind == JsonValueKind.Array)
                GatherCollectsIn(steps, collects);

        if (owner.TryGetProperty("defer", out var deferBody) && deferBody.ValueKind == JsonValueKind.Object
            && deferBody.TryGetProperty("steps", out var deferSteps) && deferSteps.ValueKind == JsonValueKind.Array)
            GatherCollectsIn(deferSteps, collects);

        if (owner.TryGetProperty("deliver", out var deliver) && deliver.ValueKind == JsonValueKind.Object)
            GatherCollects(deliver, collects);
    }
    private static void GatherCollectsIn(JsonElement steps, HashSet<string> collects)
    {
        foreach (var step in steps.EnumerateArray())
        {
            GatherCollectsOf(step, collects);

            if ((step.TryGetProperty("foreach", out _) || step.TryGetProperty("items", out _))
                && step.TryGetProperty("do", out var body) && body.ValueKind == JsonValueKind.Array)
                GatherCollectsIn(body, collects);
        }
    }

    /// <summary>单步的收集声明（字符串 = 槽名简写；数组 = <c>{ slot, from? }</c>）。</summary>
    private static void GatherCollectsOf(JsonElement step, HashSet<string> collects)
    {
        if (!step.TryGetProperty("collect", out var declared)) return;

        if (declared.ValueKind == JsonValueKind.String)
        {
            if (declared.GetString() is { Length: > 0 } bare) collects.Add(bare);
            return;
        }

        if (declared.ValueKind != JsonValueKind.Array) return;

        foreach (var item in declared.EnumerateArray())
            if (String(item, "slot") is { Length: > 0 } slot)
                collects.Add(slot);
    }


    // ── 表达式：键闭包 + 算子可达 + 平铺嵌套 ────────────────────────────────

    /// <summary>
    /// 表达式遍历：① 键闭包（算子键 ∪ 取值来源键）；② <see cref="LintPolicy.Unreachable"/> 命中的运行期不可达算子；
    /// ③ <see cref="LintPolicy.NestedGroups"/> 命中的平铺失效；④ 收集槽引用；⑤ 递归下钻（含 <c>from</c> 的对象转取值来源）。
    /// </summary>
    private static void WalkExpr(
        Sink sink, string path, JsonElement expr, HashSet<string>? slots = null, List<string>? loops = null,
        string? context = null)
    {
        if (expr.ValueKind != JsonValueKind.Object) return;

        // 表达式键域：标准词表（算子键 ∪ 取值来源键）为**规范拼写**；运行期镜像另接受若干同义拼写（如 allOf/anyOf），
        // 故此处只提示不阻断（`expr-key-unknown`）—— 阻断口径由构建期门承担。
        foreach (var member in expr.EnumerateObject())
            if (!ExprKeys.Contains(member.Name, StringComparer.Ordinal))
                sink.Add("expr-key-unknown", $"{path}.{member.Name}", member.Name, string.Join("|", ExprKeys));

        sink.Word($"{path}.aggOp", String(expr, "aggOp"), PortImplVocabulary.AggOps);

        CheckNestedGroups(sink, path, expr);
        CheckUnreachable(sink, path, expr);
        CheckQuantifierOverScalar(sink, path, expr);
        CheckCollectReferences(sink, path, expr, context);

        foreach (var member in expr.EnumerateObject())
        {
            if (member.Value.ValueKind == JsonValueKind.Object)
            {
                if (Has(member.Value, "from"))
                {
                    CheckValueSource(sink, $"{path}.{member.Name}", member.Value, context, slots, loops);
                    continue;
                }

                // 表达式内联调用（`invoke`）：键域 = 调用键 ∪ 取值来源键；实参下钻。
                if (string.Equals(member.Name, "invoke", StringComparison.Ordinal))
                {
                    CheckInvoke(sink, $"{path}.invoke", member.Value, slots, loops, context);
                    continue;
                }

                WalkExpr(sink, $"{path}.{member.Name}", member.Value, slots, loops, context);
                continue;
            }

            if (member.Value.ValueKind != JsonValueKind.Array) continue;

            var index = 0;
            foreach (var item in member.Value.EnumerateArray())
            {
                var itemPath = $"{path}.{member.Name}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object) continue;

                if (Has(item, "from")) CheckValueSource(sink, itemPath, item, context, slots, loops);
                else WalkExpr(sink, itemPath, item, slots, loops, context);
            }
        }
    }

    /// <summary>表达式内联调用（<c>invoke</c>）：键域 = 调用键 ∪ 取值来源键（<c>on</c> / <c>expect</c> / <c>out</c> 等）。</summary>
    private static void CheckInvoke(
        Sink sink, string path, JsonElement invoke, HashSet<string>? slots, List<string>? loops, string? context)
    {
        sink.Keys(path, invoke, InvokeExprKeys);
        if (invoke.TryGetProperty("on", out var target) && target.ValueKind == JsonValueKind.Object)
            CheckValueSource(sink, $"{path}.on", target, context, slots, loops);

        CheckArgArray(sink, $"{path}.args", Elements(invoke, "args"), StandardDescriptors.ValueSource.Keys,
            slots, loops, context);
    }

    /// <summary>平铺组（D-12）：宿主键与卫星键同为兄弟 ⇒ 卫星失效（策略单源 = 数据视图）。</summary>
    private static void CheckNestedGroups(Sink sink, string path, JsonElement expr)
    {
        foreach (var group in sink.Policy.NestedGroups)
        {
            var host = group.Hosts.FirstOrDefault(name => Has(expr, name));
            if (host is null) continue;

            foreach (var key in group.Keys.Where(name => Has(expr, name)))
                sink.Add("flat-nested", $"{path}.{key}", key, host, group.Defect);
        }
    }

    /// <summary>运行期不可达算子（D-10 数组合取 / D-11 <c>countEqual</c>；策略单源 = 数据视图）。</summary>
    private static void CheckUnreachable(Sink sink, string path, JsonElement expr)
    {
        foreach (var rule in sink.Policy.Unreachable)
        {
            if (!expr.TryGetProperty(rule.Key, out var value)) continue;
            if (string.Equals(rule.Form, "array", StringComparison.Ordinal)
                && value.ValueKind != JsonValueKind.Array) continue;

            sink.Add("unreachable-operator", $"{path}.{rule.Key}", rule.Key, rule.Defect, rule.Hint);
        }
    }

    /// <summary>量词 <c>over</c> 指向单元素来源（行 / 遍历项）：若其值为字符串将按字符展开（D-12 第三例）。</summary>
    private static void CheckQuantifierOverScalar(Sink sink, string path, JsonElement expr)
    {
        foreach (var name in new[] { "all", "any" })
        {
            if (!expr.TryGetProperty(name, out var quantifier) || quantifier.ValueKind != JsonValueKind.Object) continue;
            if (!quantifier.TryGetProperty("over", out var over)) continue;

            var from = String(over, "from");
            if (from is "row" or "item") sink.Add("quantifier-over-scalar", $"{path}.{name}.over", from, "D-12");
        }
    }

    /// <summary>表达式内的收集槽引用（<c>collect</c> / <c>sameAsLast</c> 必须是已声明的收集槽）。</summary>
    private static void CheckCollectReferences(Sink sink, string path, JsonElement expr, string? context)
    {
        if (sink.Collects is not { } collects) return;

        foreach (var name in new[] { "collect", "sameAsLast" })
        {
            if (String(expr, name) is not { Length: > 0 } slot) continue;
            if (collects.Contains(slot)) continue;
            sink.Add("expr-collect-undeclared", $"{path}.{name}", slot, context: context);
        }
    }

    // ── 取值来源 ────────────────────────────────────────────────────────────

    /// <summary>取值来源数组校验（逐项：键闭包由 <paramref name="allowed"/> 指定；含 <c>from</c> → 走来源校验）。</summary>
    private static void CheckArgArray(
        Sink sink, string path, IReadOnlyList<JsonElement> items, IReadOnlyList<string>? allowed,
        HashSet<string>? slots = null, List<string>? loops = null, string? context = null, bool valueSource = true)
    {
        var index = 0;
        foreach (var item in items)
        {
            var itemPath = $"{path}[{index++}]";
            if (allowed is not null) sink.Keys(itemPath, item, allowed);
            CheckValueSource(sink, itemPath, item, context, slots, loops, valueSource);
        }
    }

    /// <summary>
    /// 取值来源校验（口径对齐守卫 <c>CheckArg</c>）：取值域词表 → <c>from</c> / <c>convert</c> 词表 →
    /// 槽（<c>var</c>）/ 遍历变量（<c>item</c>）/ 依赖下标（<c>dep</c>）/ <c>with</c> 键非空 → 构造体下钻。
    /// <para><paramref name="valueSource"/> = false 用于**组合/反射族实参**（其形态为 <c>argIndex</c> / <c>convert</c>，
    /// 无 <c>from</c> 语义，故不适用 <c>from-missing</c> 判据）。</para>
    /// </summary>
    private static void CheckValueSource(
        Sink sink, string path, JsonElement source, string? context = null, HashSet<string>? slots = null,
        List<string>? loops = null, bool valueSource = true)
    {
        if (source.ValueKind != JsonValueKind.Object) return;

        if (valueSource) sink.Keys(path, source, StandardDescriptors.ValueSource.Keys);

        var from = String(source, "from");
        if (valueSource)
        {
            sink.Word($"{path}.from", from, PortImplVocabulary.ValueSourceFromAll);
            sink.Word($"{path}.convert", String(source, "convert"), PortImplVocabulary.Conversions);
        }

        foreach (var rule in sink.Policy.Unreachable)
            if (Has(source, rule.Key))
                sink.Add("unreachable-operator", $"{path}.{rule.Key}", rule.Key, rule.Defect, rule.Hint);

        if (from is not null and not "")
            CheckFrom(sink, path, source, from, context, slots, loops);
        else if (valueSource && !Truthy(source, "optional") && !Has(source, "new") && !Has(source, "construct")
                 && !Has(source, "value") && !Has(source, "delegate"))
            sink.Add("from-missing", path, context: context);

        // 构造体（E5：`from` 缺省按「new > delegate > value」推断；`new` 与 `construct` 两键同义）
        if (source.TryGetProperty("new", out var inlineNew) && inlineNew.ValueKind == JsonValueKind.Object)
            CheckConstruct(sink, $"{path}.new", inlineNew, slots, loops, context);

        if (source.TryGetProperty("construct", out var construct) && construct.ValueKind == JsonValueKind.Object)
            CheckConstruct(sink, $"{path}.construct", construct, slots, loops, context);

        if (source.TryGetProperty("delegate", out var handler) && handler.ValueKind == JsonValueKind.Object)
            CheckDelegate(sink, $"{path}.delegate", handler, slots, loops, context);
    }

    /// <summary><c>from</c> 分支：槽 / 遍历变量 / 依赖下标 / 声明缺项（逐条对齐守卫口径）。</summary>
    private static void CheckFrom(
        Sink sink, string path, JsonElement source, string from, string? context, HashSet<string>? slots,
        List<string>? loops)
    {
        switch (from)
        {
            case "var" when String(source, "key") is not { Length: > 0 }:
                sink.Add("from-var-key-missing", path, context: context);
                break;

            case "var" when slots is not null && !slots.Contains(String(source, "key")!):
                sink.Add("slot-undefined", path, String(source, "key"), context: context);
                break;

            case "item" when loops is not null && String(source, "key") is { Length: > 0 } loop
                             && !loops.Contains(loop):
                sink.Add("loop-var-undefined", path, loop, context: context);
                break;

            case "dep":
            {
                var index = Number(source, "dep");
                if (index is null || index < 0 || index >= sink.DepCount)
                    sink.Add("dep-index-out-of-range", path, index?.ToString() ?? "(空)", sink.DepCount,
                        context: context);
                break;
            }

            case "with" when String(source, "key") is not { Length: > 0 }:
                sink.Add("with-key-missing", path, context: context);
                break;

            case "new" when !Has(source, "construct"):
                sink.Add("new-construct-missing", path, context: context);
                break;

            case "type" when String(source, "type") is not { Length: > 0 }:
                sink.Add("type-name-missing", path, context: context);
                break;

            case "delegate" when !Has(source, "delegate"):
                sink.Add("delegate-missing", path, context: context);
                break;
        }
    }


    /// <summary>委托声明（<c>delegate</c>）：键闭包 + 体步骤（声明的形参名并入遍历变量集）。</summary>
    private static void CheckDelegate(
        Sink sink, string path, JsonElement handler, HashSet<string>? slots, List<string>? loops, string? context)
    {
        sink.Keys(path, handler, StandardDescriptors.Delegate.Keys);

        if (String(handler, "kind") is not { Length: > 0 }) sink.Add("delegate-kind-missing", path, context: context);

        var names = Elements(handler, "parameters").Select(item => item.GetString())
            .Where(name => name is { Length: > 0 }).Select(name => name!).ToArray();

        var added = 0;
        if (loops is not null)
            foreach (var name in names)
            {
                loops.Add(name);
                added++;
            }

        var collects = new HashSet<string>(StringComparer.Ordinal);
        if (String(handler, "collect") is { Length: > 0 } slot) collects.Add(slot);

        var previous = sink.Collects;
        sink.Collects = collects;
        try
        {
            if (handler.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                WalkSteps(sink, steps, slots ?? [], loops ?? [], collects, context, $"{path}.steps");

            if (handler.TryGetProperty("result", out var result))
                WalkExpr(sink, $"{path}.result", result, slots, loops, context);
        }
        finally
        {
            sink.Collects = previous;
            if (loops is not null)
                for (var index = 0; index < added; index++) loops.RemoveAt(loops.Count - 1);
        }
    }

    /// <summary><c>bind</c> 的 <c>with</c> 来源键须落在端口 <c>in</c> 面（提示级：真源在用例 <c>with</c> 实参）。</summary>
    private static void CheckWithKey(Sink sink, string path, JsonElement source, IReadOnlyList<string> inKeys)
    {
        if (String(source, "from") != "with") return;
        if (String(source, "key") is not { Length: > 0 } key) return;
        if (inKeys.Contains(key, StringComparer.Ordinal)) return;

        sink.Add("with-key-undeclared", path, key, string.Join("|", inKeys));
    }

    // ── 报告 ────────────────────────────────────────────────────────────────

    private static int Report(LedgerView view, LintPolicy policy, List<LintIssue> issues, int keys, int shards)
    {
        Console.WriteLine(string.Format(view.Message("lintHeader"), keys, shards));

        var errors = 0;
        var warnings = 0;

        foreach (var issue in issues
                     .OrderBy(item => item.PortKey, StringComparer.Ordinal)
                     .ThenBy(item => item.Path, StringComparer.Ordinal)
                     .ThenBy(item => item.Rule, StringComparer.Ordinal))
        {
            if (LintPolicy.IsError(issue.Severity)) errors++;
            else warnings++;

            Console.WriteLine(string.Format(view.Message("lintRow"), issue.Severity, issue.PortKey, issue.Path,
                issue.Rule, issue.Message));
        }

        Console.WriteLine(string.Format(view.Message("lintSummary"), errors, warnings));
        Console.WriteLine(view.Message("lintHint"));

        if (errors == 0)
        {
            Console.WriteLine(string.Format(view.Message("lintOk"), warnings));
            return 0;
        }

        Console.Error.WriteLine(string.Format(view.Message("lintFailed"), errors));
        return 1;
    }

    /// <summary>表达式对象键域 = 算子键 ∪ 取值来源键（两者并集 = 结构门 <c>$defs/expr/properties</c> 的键集）。</summary>
    private static IReadOnlyList<string> ExprKeys => _exprKeys ??= DeriveExprKeys();

    private static IReadOnlyList<string>? _exprKeys;

    private static IReadOnlyList<string> DeriveExprKeys()
    {
        var keys = new List<string>(PortImplVocabulary.ExprOperatorKeys);
        var seen = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var key in StandardDescriptors.ValueSource.Keys)
            if (seen.Add(key))
                keys.Add(key);

        return keys;
    }

    /// <summary>脚本族投影项键域（<c>FieldExprKeys</c> + <c>spread</c> 纯动态键投影；读取器单点支持）。</summary>
    private static IReadOnlyList<string> ProjectFieldItemKeys => _projectFieldItemKeys ??=
        [.. StandardDescriptors.FieldExpr.Keys, "spread"];

    private static IReadOnlyList<string>? _projectFieldItemKeys;

    /// <summary>表达式内联调用键域 = 调用键 ∪ 取值来源键（<c>invoke</c> 对象在表达式层同样按取值来源键取值）。</summary>
    private static IReadOnlyList<string> InvokeExprKeys => _invokeExprKeys ??= DeriveInvokeExprKeys();

    private static IReadOnlyList<string>? _invokeExprKeys;

    private static IReadOnlyList<string> DeriveInvokeExprKeys()
    {
        var keys = new List<string>(StandardDescriptors.Invoke.Keys);
        var seen = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var key in StandardDescriptors.ValueSource.Keys)
            if (seen.Add(key))
                keys.Add(key);

        return keys;
    }

    // ── JSON 小件 ────────────────────────────────────────────────────────────

    private static IEnumerable<JsonProperty> Members(JsonElement node)
        => node.ValueKind == JsonValueKind.Object ? node.EnumerateObject() : [];

    private static IEnumerable<JsonProperty> Members(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object
           && owner.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.Object
            ? node.EnumerateObject()
            : [];

    private static IReadOnlyList<JsonElement> Elements(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object
           && owner.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().ToArray()
            : [];

    private static bool Has(JsonElement node, string name)
        => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out _);

    private static string? String(JsonElement node, string name)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement node, string name)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool Truthy(JsonElement node, string name)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.True;

    /// <summary>问题汇（端口键 + 政策 + 依赖数 + 当前收集槽集；文案经 <see cref="LintPolicy.Text"/>）。</summary>
    private sealed class Sink
    {
        private readonly List<LintIssue> _issues;

        internal Sink(LintPolicy policy, List<LintIssue> issues, string portKey)
        {
            Policy = policy;
            _issues = issues;
            PortKey = portKey;
        }

        internal LintPolicy Policy { get; }

        internal string PortKey { get; }

        /// <summary>本端口依赖声明数（<c>dep</c> 下标上界）。</summary>
        internal int DepCount { get; set; }

        /// <summary>当前脚本体的收集槽集（收集槽预建 ⇒ 表达式可直接引用）。</summary>
        internal HashSet<string>? Collects { get; set; }

        /// <summary>登记一条问题（<paramref name="context"/> 非空 = 严重度按 <c>规则@上下文</c> 取）。</summary>
        internal void Add(string rule, string path, object? arg0 = null, object? arg1 = null, object? arg2 = null,
            string? context = null)
        {
            var arguments = new[] { arg0, arg1, arg2 }.Where(value => value is not null).ToArray();
            var severity = Policy.SeverityOf(context is { Length: > 0 } ? $"{rule}@{context}" : rule);
            _issues.Add(new LintIssue(PortKey, rule, severity, path, Policy.Text(rule, arguments)));
        }

        internal void Keys(string path, JsonElement node, IReadOnlyList<string> allowed)
        {
            foreach (var member in Members(node))
                if (!allowed.Contains(member.Name, StringComparer.Ordinal))
                    Add("key-unknown", $"{path}.{member.Name}", member.Name, string.Join("|", allowed));
        }

        internal void Word(string path, string? value, IReadOnlyList<string> allowed)
        {
            if (string.IsNullOrEmpty(value) || allowed.Contains(value, StringComparer.Ordinal)) return;
            Add("word-unknown", path, path, value, string.Join("|", allowed));
        }
    }
}

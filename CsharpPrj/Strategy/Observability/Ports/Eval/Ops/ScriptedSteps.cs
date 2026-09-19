namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 脚本步骤执行单源（J2-4 前置 · **通用框架**）：步骤序列的**唯一实现点** ——
/// <c>New</c>（构造）/ <c>Call</c>（调用）/ <c>Loop</c>（遍历）/ <c>Collect</c>（收集）四类步骤按声明序执行，
/// <c>When</c> 条件短路与 <c>Collects</c> 收集为**步骤修饰**（与指令同体承载，不另立指令）。
/// <para>
/// 两个消费方共用本件 ⇒ 语义不可能分叉：① 旧绑定类（③ 声明经 <see cref="PortSpecCompiler.Steps"/> 编译为指令后调用，
/// 退化为薄编排）；② 求值引擎（<see cref="PortOp.New"/> / <see cref="PortOp.Call"/> / <see cref="PortOp.Loop"/>
/// / <see cref="PortOp.Collect"/> 臂）。
/// </para>
/// <para>诊断口径（RF-5）：异常消息与迁移前逐字一致（含 ⊥ 留痕文案与遍历 / 步骤定位）。</para>
/// </summary>
internal static class ScriptedSteps
{
    /// <summary>
    /// 执行宿主：端口键（诊断定位）+ 依赖**声明**（实例调用的静态类型解析）+ 留痕出口
    /// （⊥ 记录按调用回传，供诊断面读取；不得跨调用串台）。
    /// </summary>
    internal sealed record Host(
        string PortKey, IReadOnlyList<DepStandard> Deps, Action<IReadOnlyList<string>> Notes);

    /// <summary>按声明序执行步骤（<c>When</c> 为假即短路跳过；形态未知 → fail-fast）。返回末步产物值。</summary>
    internal static object? Run(
        Host host, IReadOnlyList<PortInstr> steps, EvalScope scope, IPortInput input, object?[] deps)
    {
        object? last = null;
        var index = 0;

        foreach (var step in steps)
        {
            index++;

            if (step.When is { } condition)
            {
                var gate = ScriptedExprEvaluator.Evaluate(condition, scope, input, deps, "步骤条件 when");

                // W1 + E8：⊥（缺槽 / 解析失败）语义 = 「不执行且记录」——留痕含定位（端口键 / 步骤序号 / ⊥ 出处），不静默。
                if (gate is null)
                    scope.Note(
                        $"端口 '{scope.PortKey}' 步骤 {index} 条件 when 求值为 ⊥" +
                        $"（{scope.TakeBottom() ?? "缺值 / 解析失败"}）：本步骤不执行");

                if (!PortValues.Truthy(gate)) continue;
            }

            if (step.Op == PortOp.New && step.Operand is NewStandard construct)
                last = RunNew(host, step, construct, scope, input, deps);
            else if (step.Op == PortOp.Call && step.Operand is InvokeStandard invoke)
                last = RunInvoke(host, step, invoke, scope, input, deps);
            else if (step.Op == PortOp.Loop && step.Operand is ForeachStandard loop)
                last = RunForeach(host, step, loop, scope, input, deps);
            else
                throw new PortBindingViolationException(
                    host.PortKey, $"端口 '{host.PortKey}' 的脚本步骤未声明形态（new|invoke|foreach）");
        }

        host.Notes(scope.Notes);
        return last;
    }

    /// <summary>构造步骤：反射构造（单源 <see cref="ScriptedConstruction"/>）→ 写槽 → 收集。返回构造对象。</summary>
    internal static object? RunNew(
        Host host, PortInstr step, NewStandard construct, EvalScope scope, IPortInput input, object?[] deps)
    {
        var created = ScriptedConstruction.Build(construct, scope, input, deps, $"步骤 new '{construct.As}'");

        if (!string.IsNullOrWhiteSpace(construct.As)) scope.Set(construct.As, created);
        Collect(step, created, scope);
        return created;
    }

    /// <summary>调用步骤：执行（调用单源 <see cref="ScriptedInvokeRunner"/>）→ 写槽 / 出参捕获 / 收集 → 可选跳出遍历。</summary>
    internal static object? RunInvoke(
        Host host, PortInstr step, InvokeStandard invoke, EvalScope scope, IPortInput input, object?[] deps)
    {
        var context = $"步骤 invoke '{invoke.Method}'";
        var outcome = ScriptedInvokeRunner.Run(host.PortKey, invoke, host.Deps, scope, input, deps);

        if (!invoke.IsProperty)
            ScriptedInvokeRunner.CaptureOut(host.PortKey, invoke, outcome.Parameters, outcome.Args, scope, context);

        if (invoke.As is { Length: > 0 } asName) scope.Set(asName, outcome.Result);

        // break 语义：声明 expect 时「命中即跳出」（首遇失败即停）；未声明 expect 时执行后即跳出。
        if (invoke.Break && ScriptedInvokeRunner.ShouldBreak(invoke, outcome.Result)) throw new BreakSignal();

        // 收集置于跳出判定之后：被 break 中止的迭代「未完成」→ 其收集不入列
        // （夹具「仅登记成功者入列」等口径由此对齐；foreach 级收集仍记录本次迭代末产物，见 RunForeach）。
        Collect(step, outcome.Result, scope);

        return outcome.Result;
    }

    /// <summary>遍历步骤：取集合（槽 / 表达式）逐项入栈循环变量 → 过滤 → 执行体内步骤（<c>break</c> 跳出本层）。返回末次迭代产物值。</summary>
    internal static object? RunForeach(
        Host host, PortInstr step, ForeachStandard loop, EvalScope scope, IPortInput input, object?[] deps)
    {
        if (string.IsNullOrWhiteSpace(loop.As))
            throw new PortBindingViolationException(
                host.PortKey, $"端口 '{host.PortKey}' 的遍历步骤缺少 as（循环变量名）");

        var context = $"遍历 '{loop.As}'";
        var items = loop.Slot is { Length: > 0 } slotName
            ? scope.Slot(slotName, context)
            : ScriptedExprEvaluator.Evaluate(
                loop.Items ?? throw new PortBindingViolationException(
                    host.PortKey, $"端口 '{host.PortKey}' 的 {context} 缺集合来源（slot 或 items）"),
                scope, input, deps, context);

        var body = step.Body ?? Array.Empty<PortInstr>();
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
                    scope.Note(
                        $"端口 '{scope.PortKey}' 的 {context} 第 {index} 次迭代的过滤条件求值为 ⊥" +
                        $"（{scope.TakeBottom() ?? "缺值 / 解析失败"}）：本次迭代不执行");

                if (PortValues.Truthy(gate))
                {
                    produced = Run(host, body, scope, input, deps);

                    // foreach 级收集只记录本迭代 body 实际执行后的产物（与「break 迭代未完成不入列」同一口径）。
                    scope.Pop();
                    Collect(step, produced, scope);
                }
                else
                {
                    // where 过滤：body 未执行 → 本次迭代无产物，不收集（旧口径收上一轮末产物 → 幻影重复 / 首元素 null）。
                    scope.Pop();
                }
            }
            catch (BreakSignal)
            {
                scope.Pop();
                Collect(step, produced, scope);
                return produced;
            }

            last = produced;
            index++;
        }

        return last;
    }

    /// <summary>
    /// 值源步骤（<c>Table</c> / <c>Presence</c>）：求值声明值源（行表 / 词元集合）并写槽（<c>Out</c>）。
    /// 求值单源 = <see cref="ScriptedValue"/>（与实参位 / 绑定位的值源口径一致）。
    /// </summary>
    internal static object? RunValueSource(
        Host host, PortInstr step, ValueSourceStandard source, EvalScope scope, IPortInput input, object?[] deps)
    {
        var value = ScriptedValue.Resolve(source, scope, input, deps, $"步骤 {step.Op}");
        if (!string.IsNullOrWhiteSpace(step.Out)) scope.Set(step.Out!, value);

        Collect(step, value, scope);
        return value;
    }

    /// <summary>收集声明（可多源：步产物 ∪ 指定槽当前值）。</summary>
    internal static void Collect(PortInstr step, object? product, EvalScope scope)
    {
        if (step.Collects is not { Count: > 0 } collects) return;

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
    internal static IReadOnlySet<string> Collections(
        IReadOnlyList<PortInstr> program, IReadOnlyList<FieldExprStandard> project)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectSteps(program, names);

        foreach (var field in project) CollectExpr(field.Expr, names);

        return names;
    }

    private static void CollectSteps(IReadOnlyList<PortInstr> steps, HashSet<string> names)
    {
        foreach (var step in steps)
        {
            foreach (var collect in step.Collects ?? Array.Empty<CollectStandard>()) names.Add(collect.Slot);
            CollectSteps(step.Body ?? Array.Empty<PortInstr>(), names);
        }
    }

    private static void CollectExpr(ExprStandard expr, HashSet<string> names)
    {
        if (expr.Collect is { Length: > 0 } collect) names.Add(collect);
        if (expr.SameAsLast is { Length: > 0 } other) names.Add(other);

        foreach (var sub in Subs(expr)) CollectExpr(sub, names);
    }

    private static IEnumerable<ExprStandard> Subs(ExprStandard expr)
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

        foreach (var item in expr.Concat ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.CountEqual ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.AllOf ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.AnyOf ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.Coalesce ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.SameReference ?? Array.Empty<ExprStandard>()) yield return item;
        foreach (var item in expr.SequenceEqual ?? Array.Empty<ExprStandard>()) yield return item;

        if (expr.Distinct is { } distinct) yield return distinct;
        if (expr.Fold is { } fold) yield return fold;

        foreach (var part in expr.Parts ?? Array.Empty<ConcatPartStandard>())
        {
            yield return part.Expr;
            if (part.When is { } gate) yield return gate;
        }
    }
}

using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// 端口 → 产品符号 边提取（L3 关系层）：把 <c>impl</c> 声明面里出现的**目标类型 / 成员**抽成符号节点，
/// 使影响分析能回答「改这个产品符号 → 波及哪些端口」。
/// <para>
/// 单源纪律：只读标准目录（<see cref="IStandardCatalog.Ports"/> 与其 <c>PortImplStandard</c> 子模型），
/// **不另造实体表**；符号 id 为不透明字符串，仅 Ordinal 比较。零领域词汇、零磁盘写、零执行。
/// </para>
/// <para>
/// 覆盖声明面全部出现位：<c>target</c>·<c>method</c>；<c>deps[].new</c>/<c>.contract</c>/<c>.elementType</c>；
/// <c>calls[]</c>；<c>script</c> 步骤（<c>new</c>/<c>invoke</c>/<c>foreach</c>）；<c>deliver.capture</c>·<c>deliver.deferSteps</c>；
/// <c>scenarios.cases[*]</c>；取值来源内嵌 <c>construct</c>·<c>delegate</c>；表达式（含内联 <c>invoke</c>、<c>fold.step</c> 链、<c>concat</c> 段）；
/// 构造 <c>chain</c>（归属宿主类型）；<c>init</c>；<c>scopes[].invoke</c>·<c>save</c>·<c>open</c>·<c>close</c>·<c>files</c>。
/// </para>
/// </summary>
public static class StandardPortRelations
{
    /// <summary>端口 → 产品符号 边（端口键序 → 声明序；同键同符号去重）。</summary>
    public static IReadOnlyList<RelationEdge> PortSymbolEdges(IStandardCatalog catalog)
    {
        var edges = new List<RelationEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var port in catalog.Ports)
        {
            if (port.Impl is not { } impl) continue;

            var from = StandardRelations.PortId(port.Key);
            var symbols = new List<string>();
            CollectImpl(impl, symbols);

            foreach (var symbol in symbols)
                if (seen.Add(from + "\u0000" + symbol))
                    edges.Add(new RelationEdge(from, StandardRelations.SymbolId(symbol), RelationKind.PortSymbol));
        }

        return edges;
    }

    // ── 声明面遍历（逐位收符号；声明序 = 出现序） ────────────────────────────

    private static void CollectImpl(PortImplStandard impl, List<string> symbols)
    {
        Member(symbols, impl.Target, impl.Method);
        CollectDeps(impl.Deps, symbols);
        CollectCalls(impl.Calls, symbols);
        CollectSteps(impl.Script, symbols);

        if (impl.Deliver is { } deliver)
        {
            CollectSteps(deliver.DeferSteps, symbols);
            if (deliver.Capture is { } capture) CollectInvoke(capture.Call, symbols);
        }

        CollectScopes(impl.Scopes, symbols);

        if (impl.Scenarios is not { } scenarios) return;

        foreach (var body in scenarios.Cases.Values)
        {
            CollectDeps(body.Deps, symbols);
            CollectSteps(body.Script, symbols);
            if (body.Deliver is { } scenarioDeliver) CollectSteps(scenarioDeliver.DeferSteps, symbols);
            CollectScopes(body.Scopes, symbols);
        }
    }

    private static void CollectDeps(IReadOnlyList<DepStandard>? deps, List<string> symbols)
    {
        if (deps is null) return;

        foreach (var dep in deps)
        {
            Type(symbols, dep.New);

            // 多契约（E2-b）：`|` 分隔的接口名逐个入边（单契约 = 原样）。
            if (dep.Contract is { Length: > 0 } contract)
            {
                foreach (var name in contract.Split(
                             '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Type(symbols, name);
            }

            CollectComposedArgs(dep.Args, symbols);
        }
    }

    private static void CollectCalls(IReadOnlyList<CallStandard>? calls, List<string> symbols)
    {
        if (calls is null) return;

        foreach (var call in calls)
        {
            Member(symbols, call.Target, call.Method);
            CollectComposedArgs(call.Args, symbols);
        }
    }

    private static void CollectComposedArgs(IReadOnlyList<ComposedArgStandard>? args, List<string> symbols)
    {
        if (args is null) return;

        foreach (var arg in args) Type(symbols, arg.ElementType);
    }

    private static void CollectSteps(IReadOnlyList<StepStandard>? steps, List<string> symbols)
    {
        if (steps is null) return;

        foreach (var step in steps)
        {
            CollectExpr(step.When, symbols);
            CollectExpr(step.Expr, symbols);
            if (step.New is { } construct) CollectConstruct(construct, symbols);
            if (step.Invoke is { } invoke) CollectInvoke(invoke, symbols);

            if (step.Foreach is not { } loop) continue;

            CollectExpr(loop.Items, symbols);
            CollectExpr(loop.Where, symbols);
            CollectSteps(loop.Do, symbols);
        }
    }

    private static void CollectConstruct(NewStandard construct, List<string> symbols)
    {
        Type(symbols, construct.Type);
        CollectValues(construct.Args, symbols);

        foreach (var link in construct.Chain ?? [])
        {
            Member(symbols, construct.Type, link.Method);
            CollectValues(link.Args, symbols);
        }

        foreach (var init in construct.Init ?? []) CollectValueSource(init.Value, symbols);
    }

    private static void CollectInvoke(InvokeStandard invoke, List<string> symbols)
    {
        Member(symbols, invoke.Target, invoke.Method);
        CollectValues(invoke.Args, symbols);
        CollectValueSource(invoke.Value, symbols);
    }

    private static void CollectScopes(IReadOnlyList<ScopeStandard>? scopes, List<string> symbols)
    {
        if (scopes is null) return;

        foreach (var scope in scopes)
        {
            if (scope.Invoke is { } invoke) CollectInvoke(invoke, symbols);
            foreach (var save in scope.Save ?? [])
            {
                Member(symbols, save.Type, save.Key);
                CollectValueSource(save.Set, symbols);
            }
            CollectSteps(scope.Open, symbols);
            CollectSteps(scope.Close, symbols);

            foreach (var file in scope.Files ?? [])
            {
                CollectValueSource(file.From, symbols);
                CollectValueSource(file.Batch, symbols);
                CollectExpr(file.Dir, symbols);
                CollectExpr(file.Content, symbols);
                CollectExpr(file.NameExpr, symbols);
                CollectExpr(file.When, symbols);
            }
        }
    }

    private static void CollectValues(IReadOnlyList<ValueSourceStandard>? values, List<string> symbols)
    {
        if (values is null) return;

        foreach (var value in values) CollectValueSource(value, symbols);
    }

    private static void CollectValueSource(ValueSourceStandard? source, List<string> symbols)
    {
        if (source is null) return;

        Type(symbols, source.Type);
        Type(symbols, source.ElementType);
        if (source.Construct is { } construct) CollectConstruct(construct, symbols);

        if (source.Delegate is { } handler)
        {
            Type(symbols, handler.Kind);
            CollectSteps(handler.Steps, symbols);
            CollectExpr(handler.Result, symbols);
        }

        CollectValueSource(source.Rows, symbols);
        CollectValueSource(source.Items, symbols);
        CollectExpr(source.RowValue, symbols);
        CollectExpr(source.Filter, symbols);
    }

    /// <summary>表达式遍历（成员表单源 = <see cref="PortImplVocabulary.ExprSlots"/>；取值 = <c>ExprStandard</c> 同名属性）。</summary>
    private static void CollectExpr(ExprStandard? expr, List<string> symbols)
    {
        if (expr is null) return;

        foreach (var slot in PortImplVocabulary.ExprSlots)
        {
            var value = typeof(ExprStandard).GetProperty(slot.Member)?.GetValue(expr);
            if (value is null) continue;

            switch (slot.Kind)
            {
                case ExprSlotKind.One:
                    if (value is ExprStandard single) CollectExpr(single, symbols);
                    break;

                case ExprSlotKind.Many:
                    if (value is IEnumerable<ExprStandard> many)
                        foreach (var item in many)
                            CollectExpr(item, symbols);
                    break;

                case ExprSlotKind.Parts:
                    if (value is IEnumerable<ConcatPartStandard> parts)
                        foreach (var part in parts)
                        {
                            CollectExpr(part.Expr, symbols);
                            CollectExpr(part.When, symbols);
                        }

                    break;

                case ExprSlotKind.Source:
                    if (value is ValueSourceStandard source) CollectValueSource(source, symbols);
                    break;

                case ExprSlotKind.Invoke:
                    if (value is InvokeStandard invoke) CollectInvoke(invoke, symbols);
                    break;

                case ExprSlotKind.Chain:
                    if (value is ChainStandard chain) CollectValues(chain.Args, symbols);
                    break;
            }
        }
    }

    // ── 符号登记（空值跳过；声明序 = 出现序） ────────────────────────────────

    private static void Type(List<string> symbols, string? type)
    {
        if (type is { Length: > 0 }) symbols.Add(type);
    }

    private static void Member(List<string> symbols, string? type, string? member)
    {
        if (type is not { Length: > 0 }) return;

        symbols.Add(member is { Length: > 0 } name ? $"{type}.{name}" : type);
    }
}

using Operation.HierarchyMethod.Nodes.Workflows;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 控制流发射器（R3）：把 <c>[Branch]/[Switch]/[Call]</c> 声明装配到 <see cref="RoundSpec"/>（中立原语），
/// 由 <c>RoundFlowSpecMapper</c> 再映射为电路配方控制流槽位。
/// <para>发射层只产**中立原语**（<see cref="RoundBranch"/>/<see cref="RoundSwitch"/>/<see cref="RoundCall"/>），
/// 不直接依赖电路框架 —— 保持「发射层 → 骨架契约」单一权威。</para>
/// </summary>
public static class UsharpControlFlowEmitter
{
    /// <summary>调用嵌套深度上限（防自引用声明导致的无界递归）。</summary>
    public const int MaxCallDepth = 8;

    /// <summary>把控制流声明装配到 <paramref name="spec"/>（缺省无声明 = 原样返回，源兼容）。</summary>
    public static RoundSpec Apply(RoundSpec spec, UsharpCompilation compilation)
        => Apply(spec, compilation, UsharpNodePortReader.Read(compilation.Declarations), depth: 1,
            diagnostics: null);

    /// <summary>
    /// 装配控制流并**收集诊断**（E-18：深度截断 / 子流步骤不可映射不再静默）。
    /// </summary>
    /// <param name="spec">待装配骨架。</param>
    /// <param name="compilation">编译结果。</param>
    /// <param name="diagnostics">诊断出口（null = 不产诊断，兼容旧调用）。</param>
    public static RoundSpec Apply(
        RoundSpec spec, UsharpCompilation compilation, ICollection<UsharpDiagnostic>? diagnostics)
        => Apply(spec, compilation, UsharpNodePortReader.Read(compilation.Declarations), depth: 1, diagnostics);

    /// <summary>装配控制流（**复用调用方已读的 <c>[Node]</c> 端口表**，避免递归内重读）。</summary>
    internal static RoundSpec Apply(
        RoundSpec spec, UsharpCompilation compilation, IReadOnlyDictionary<string, UsharpNodeSpec> nodes)
        => Apply(spec, compilation, nodes, depth: 1, diagnostics: null);

    /// <summary>装配控制流（带诊断出口；<c>UsharpRoundSpecEmitter</c> 的唯一接线点）。</summary>
    internal static RoundSpec Apply(
        RoundSpec spec, UsharpCompilation compilation, IReadOnlyDictionary<string, UsharpNodeSpec> nodes,
        ICollection<UsharpDiagnostic>? diagnostics)
        => Apply(spec, compilation, nodes, depth: 1, diagnostics);

    private static RoundSpec Apply(
        RoundSpec spec, UsharpCompilation compilation, IReadOnlyDictionary<string, UsharpNodeSpec> nodes,
        int depth, ICollection<UsharpDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(nodes);

        // 深度上限：只截断**子流装配**，本层分支/分派/受控边/受护域照常收集；截断事实可见化（US0018）。
        var withinDepth = depth <= MaxCallDepth;
        if (!withinDepth) ReportDepthTruncated(compilation, spec.Name, diagnostics);

        var branches = CollectBranches(compilation, spec.Name);
        var switches = CollectSwitches(compilation, spec.Name);
        var calls = withinDepth
            ? CollectCalls(compilation, spec.Name, nodes, depth, diagnostics)
            : new List<RoundCall>();
        var regions = withinDepth
            ? CollectRegions(compilation, spec.Name, nodes, depth, diagnostics)
            : new List<RoundRegion>();
        var whens = CollectWhens(compilation, spec.Name);
        var guards = CollectGuards(compilation, spec.Name);

        if (branches.Count == 0 && switches.Count == 0 && calls.Count == 0
            && regions.Count == 0 && whens.Count == 0 && guards.Count == 0) return spec;
        return spec with { Branches = branches, Switches = switches, Calls = calls,
            Regions = regions, Whens = whens, Guards = guards };
    }

    private static List<RoundBranch> CollectBranches(UsharpCompilation compilation, string flowId)
        => Collect(compilation, UsharpSyntaxProjector.Branches,
            b => Matches(b.FlowId, flowId) && b.Node.Length > 0,
            b => new RoundBranch(b.Node, b.JudgeKey, b.Then, b.Else));

    private static List<RoundSwitch> CollectSwitches(UsharpCompilation compilation, string flowId)
        => Collect(compilation, UsharpSyntaxProjector.Switches,
            s => Matches(s.FlowId, flowId) && s.Node.Length > 0 && s.Routes.Count > 0,
            s => new RoundSwitch(s.Node, s.SelectorKey,
                new Dictionary<string, string>(s.Routes, StringComparer.Ordinal), s.Default));

    private static List<RoundCall> CollectCalls(
        UsharpCompilation compilation, string flowId, IReadOnlyDictionary<string, UsharpNodeSpec> nodes, int depth,
        ICollection<UsharpDiagnostic>? diagnostics)
        => CollectWithChild(compilation, UsharpSyntaxProjector.Calls,
            c => Matches(c.FlowId, flowId) && c.Node.Length > 0 && c.ChildFlowId.Length > 0,
            c =>
            {
                var child = BuildChildSpec(compilation, c.ChildFlowId, nodes, depth + 1, diagnostics);
                if (child is null) return null;
                return new RoundCall(c.Node, child, c.Callee,
                    c.InBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList(),
                    c.OutBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList());
            });

    /// <summary>
    /// 子流就地发射为子骨架（<c>[Node]</c> 键 → 步骤；递归装配其自身控制流）。
    /// <para>步骤装配走**唯一映射点** <see cref="UsharpRoundSpecEmitter.BuildSteps"/>（不再自拼 <see cref="RoundStep"/>）；
    /// <paramref name="nodes"/> 由最外层一次读取后下传，递归内不重读；诊断随递归下传（不放空）。</para>
    /// </summary>
    private static RoundSpec? BuildChildSpec(
        UsharpCompilation compilation, string flowId, IReadOnlyDictionary<string, UsharpNodeSpec> nodes, int depth,
        ICollection<UsharpDiagnostic>? diagnostics)
    {
        var flow = compilation.Flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));
        if (flow is null) return null;

        var sameFileNodes = UsharpNodePortReader.Read(compilation.Declarations, flow.SourceFile);
        var steps = UsharpRoundSpecEmitter.BuildSteps(flow, nodes, sameFileNodes, diagnostics);
        var spec = new RoundSpec(flow.FlowId, steps) { Source = RoundSpecSource.Declared };
        return Apply(spec, compilation, nodes, depth, diagnostics);
    }

    /// <summary>深度截断可见化（<c>US0018</c>；归因到被截断的流声明）。</summary>
    private static void ReportDepthTruncated(
        UsharpCompilation compilation, string flowId, ICollection<UsharpDiagnostic>? diagnostics)
    {
        if (diagnostics is null) return;

        var flow = compilation.Flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));
        diagnostics.Add(UsharpDiagnostic.Warn(UsharpCodes.CallDepthTruncated,
            $"[Flow] {flowId}: 子流嵌套超过上限 {MaxCallDepth}（后续子流未装配；截断不静默）",
            flow?.SourceFile ?? string.Empty, flow?.LineNumber ?? 0));
    }

    private static List<RoundRegion> CollectRegions(
        UsharpCompilation compilation, string flowId, IReadOnlyDictionary<string, UsharpNodeSpec> nodes, int depth,
        ICollection<UsharpDiagnostic>? diagnostics)
        => CollectWithChild(compilation, UsharpSyntaxProjector.Regions,
            r => Matches(r.FlowId, flowId) && r.ParentNode.Length > 0 && r.ChildFlowId.Length > 0,
            r =>
            {
                var child = BuildChildSpec(compilation, r.ChildFlowId, nodes, depth + 1, diagnostics);
                if (child is null) return null;
                return new RoundRegion(
                    r.ParentNode,
                    child,
                    MapIteration(r.Iteration),
                    r.MaxIterations,
                    r.JudgePort,
                    r.InBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList(),
                    r.OutBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList());
            });

    private static RoundRegionIteration MapIteration(string iter)
    {
        return iter switch
        {
            UsharpLanguage.IterationWhileTrue => RoundRegionIteration.WhileTrue,
            UsharpLanguage.IterationUntilTrue => RoundRegionIteration.UntilTrue,
            _ => RoundRegionIteration.Once,
        };
    }

    private static List<RoundWhenDecl> CollectWhens(UsharpCompilation compilation, string flowId)
        => Collect(compilation, UsharpSyntaxProjector.Whens,
            w => Matches(w.FlowId, flowId)
                && w.FromNode.Length > 0 && w.To.Length > 0 && w.JudgeKey.Length > 0,
            w => new RoundWhenDecl(w.FromNode, w.To, w.JudgeKey));

    private static List<RoundGuardDecl> CollectGuards(UsharpCompilation compilation, string flowId)
        => Collect(compilation, UsharpSyntaxProjector.Guards,
            g => Matches(g.FlowId, flowId) && g.NodeIds.Count > 0,
            g => new RoundGuardDecl(g.NodeIds));

    /// <summary>
    /// 声明收集模板（D-14 单一实现）：**文档序 → 投影序列 → 过滤 → 投影**。
    /// <para>顺序与过滤逐条照旧（禁改语义）；新增控制流声明只改本处调用的三个委托。</para>
    /// </summary>
    private static List<TDto> Collect<TDecl, TDto>(
        UsharpCompilation compilation,
        Func<Plate.DslMethod.Model.DslDocument, IEnumerable<TDecl>> source,
        Func<TDecl, bool> accept,
        Func<TDecl, TDto> project)
    {
        var list = new List<TDto>();
        foreach (var doc in compilation.Documents)
            foreach (var decl in source(doc))
                if (accept(decl)) list.Add(project(decl));
        return list;
    }

    /// <summary>声明收集模板（可空投影：子流装配失败 ⇒ 该项不产出，与既有 <c>continue</c> 等价）。</summary>
    private static List<TDto> CollectWithChild<TDecl, TDto>(
        UsharpCompilation compilation,
        Func<Plate.DslMethod.Model.DslDocument, IEnumerable<TDecl>> source,
        Func<TDecl, bool> accept,
        Func<TDecl, TDto?> project)
        where TDto : class
    {
        var list = new List<TDto>();
        foreach (var doc in compilation.Documents)
            foreach (var decl in source(doc))
            {
                if (!accept(decl)) continue;
                var dto = project(decl);
                if (dto is null) continue;
                list.Add(dto);
            }
        return list;
    }

    private static bool Matches(string declaredFlow, string currentFlow)
        => string.Equals(declaredFlow, currentFlow, StringComparison.Ordinal);
}

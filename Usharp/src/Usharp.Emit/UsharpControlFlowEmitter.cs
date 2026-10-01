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
        => Apply(spec, compilation, depth: 1);

    private static RoundSpec Apply(RoundSpec spec, UsharpCompilation compilation, int depth)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(compilation);

        var branches = CollectBranches(compilation, spec.Name);
        var switches = CollectSwitches(compilation, spec.Name);
        var calls = CollectCalls(compilation, spec.Name, depth);

        if (branches.Count == 0 && switches.Count == 0 && calls.Count == 0) return spec;
        return spec with { Branches = branches, Switches = switches, Calls = calls };
    }

    private static List<RoundBranch> CollectBranches(UsharpCompilation compilation, string flowId)
    {
        var list = new List<RoundBranch>();
        foreach (var doc in compilation.Documents)
            foreach (var b in UsharpSyntaxProjector.Branches(doc))
                if (Matches(b.FlowId, flowId) && b.Node.Length > 0)
                    list.Add(new RoundBranch(b.Node, b.JudgeKey, b.Then, b.Else));
        return list;
    }

    private static List<RoundSwitch> CollectSwitches(UsharpCompilation compilation, string flowId)
    {
        var list = new List<RoundSwitch>();
        foreach (var doc in compilation.Documents)
            foreach (var s in UsharpSyntaxProjector.Switches(doc))
                if (Matches(s.FlowId, flowId) && s.Node.Length > 0 && s.Routes.Count > 0)
                    list.Add(new RoundSwitch(s.Node, s.SelectorKey,
                        new Dictionary<string, string>(s.Routes, StringComparer.Ordinal), s.Default));
        return list;
    }

    private static List<RoundCall> CollectCalls(UsharpCompilation compilation, string flowId, int depth)
    {
        var list = new List<RoundCall>();
        if (depth > MaxCallDepth) return list;
        foreach (var doc in compilation.Documents)
            foreach (var c in UsharpSyntaxProjector.Calls(doc))
            {
                if (!Matches(c.FlowId, flowId) || c.Node.Length == 0 || c.ChildFlowId.Length == 0) continue;
                var child = BuildChildSpec(compilation, c.ChildFlowId, depth + 1);
                if (child is null) continue;
                list.Add(new RoundCall(c.Node, child, c.Callee,
                    c.InBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList(),
                    c.OutBindings.Select(kv => new RoundPortBinding(kv.Key, kv.Value)).ToList()));
            }
        return list;
    }

    /// <summary>子流就地发射为子骨架（<c>[Node]</c> 键 → 步骤；递归装配其自身控制流）。</summary>
    private static RoundSpec? BuildChildSpec(UsharpCompilation compilation, string flowId, int depth)
    {
        var flow = compilation.Flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));
        if (flow is null) return null;

        var nodes = UsharpNodePortReader.Read(compilation.Declarations);
        var steps = new List<RoundStep>(flow.Steps.Count);
        foreach (var s in flow.Steps)
            if (nodes.TryGetValue(s.Key, out var n))
                steps.Add(new RoundStep(n.FuncName, n.InputPorts, n.OutputPorts, NodeId: s.Key) { Emits = s.Emits });

        var spec = new RoundSpec(flow.FlowId, steps) { Source = RoundSpecSource.Declared };
        return Apply(spec, compilation, depth);
    }

    private static bool Matches(string declaredFlow, string currentFlow)
        => string.Equals(declaredFlow, currentFlow, StringComparison.Ordinal);
}

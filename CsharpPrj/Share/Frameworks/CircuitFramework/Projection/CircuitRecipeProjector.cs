using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;
using Ckt = Share.CircuitFramework.Core.Circuit;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 装配配方投影层（读侧投影）：把声明式装配配方 <see cref="CircuitRecipeSpec"/> 投影为电路 <see cref="Ckt"/> ——
/// 步骤 = 节点 + 具名端口，产物→依赖 = Wire，入口 = EntrySymbol，最终产物 = ExitSymbol。
/// 被投影电路可交给 CircuitValidator / CircuitCompiler / CircuitCallGraph 做校验与影响面分析；
/// 只装配电路、不回写业务代码（沿「读侧投影不回写」纪律）。
/// </summary>
public static class CircuitRecipeProjector
{
    /// <summary>把声明式装配配方投影为电路。</summary>
    public static Ckt Project(CircuitRecipeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var circuit = new Ckt(spec.CircuitId);
        AppendSteps(circuit, spec.Steps);
        AppendFlows(circuit, spec.Flows);

        // 入口绑定（外部产物，如 Config）→ EntrySymbol
        foreach (var e in spec.EntryPorts)
            circuit.Append(new EntrySymbol { Port = e });

        // 最终产物（NodeId.Port）→ ExitSymbol
        foreach (var ex in spec.ExitPorts)
            circuit.Append(new ExitSymbol { Port = ex });

        return circuit;
    }

    /// <summary>步骤 → 电路节点（具名端口）。共享给其它投影器（如调用链投影），保证装配只有一份实现。</summary>
    public static void AppendSteps(Ckt circuit, IReadOnlyList<CircuitStepSpec> steps)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var s in steps)
        {
            var node = circuit.AddNode(s.Id, s.StepKind);
            foreach (var inp in s.InPorts) node.AddIn(inp);
            foreach (var outp in s.OutPorts) node.AddOut(outp);
        }
    }

    /// <summary>产物→依赖 传递边 → Wire（OutPort → InPort）。</summary>
    public static void AppendFlows(Ckt circuit, IReadOnlyList<CircuitFlowSpec> flows)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(flows);

        foreach (var f in flows)
            circuit.Wire(f.FromNode, f.FromPort, f.ToNode, f.ToPort);
    }
}

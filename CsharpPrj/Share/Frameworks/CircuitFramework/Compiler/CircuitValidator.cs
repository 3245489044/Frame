using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 编译期图校验：环检测 / 类型校验 / 端口解析 / 孤立引用 / BaseCase 强制。
/// 非法图直接编译失败，不进入执行。
/// </summary>
public static class CircuitValidator
{
    public static IReadOnlyList<CircuitDiagnostic> Validate(Circuit circuit)
    {
        var d = new List<CircuitDiagnostic>();
        var nodes = circuit.Nodes.ToDictionary(n => n.Id);

        CheckUniqueNodeIds(circuit, d);
        bool hasCycles = CheckCycles(circuit, d);
        if (!hasCycles) // 环存在时后续排序/端到端分析意义不大，但端口解析仍可进行
        {
            CheckPortResolution(circuit, nodes, d);
        }
        CheckFanIn(circuit, d);
        CheckRecursion(circuit, d);
        CheckCallTargets(circuit, d);
        return d;
    }

    public static bool IsValid(Circuit circuit) => Validate(circuit).All(x => x.Severity != DiagnosticSeverity.Error);

    private static void CheckUniqueNodeIds(Circuit circuit, List<CircuitDiagnostic> d)
    {
        var seen = new HashSet<string>();
        foreach (var n in circuit.Nodes)
        {
            if (!seen.Add(n.Id))
                d.Add(new(DiagnosticSeverity.Error, "CFG-001", $"重复的节点 Id：'{n.Id}'。", n.Id));
        }
    }

    private static bool CheckCycles(Circuit circuit, List<CircuitDiagnostic> d)
    {
        var cycles = CycleDetector.FindCycles(circuit);
        foreach (var cyc in cycles)
            d.Add(new(DiagnosticSeverity.Error, "CFG-002",
                $"Wire 依赖图存在非法环（环只允许由 Recursion 符号产生，不得以 Wire 成环）：{string.Join(" -> ", cyc)}。",
                cyc.FirstOrDefault()));
        return cycles.Count > 0;
    }

    private static void CheckPortResolution(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        foreach (var w in circuit.Wires)
        {
            if (!nodes.TryGetValue(w.From.NodeId, out var fromNode))
                d.Add(new(DiagnosticSeverity.Error, "CFG-003", $"Wire 源节点不存在：'{w.From.NodeId}'。", w.To.NodeId));
            else if (fromNode.OutPorts.All(p => p.Name != w.From.PortName))
                d.Add(new(DiagnosticSeverity.Error, "CFG-004", $"节点 '{w.From.NodeId}' 无输出端口 '{w.From.PortName}'。", w.From.NodeId));

            if (!nodes.TryGetValue(w.To.NodeId, out var toNode))
                d.Add(new(DiagnosticSeverity.Error, "CFG-005", $"Wire 目标节点不存在：'{w.To.NodeId}'。", w.To.NodeId));
            else if (toNode.InPorts.All(p => p.Name != w.To.PortName))
                d.Add(new(DiagnosticSeverity.Error, "CFG-006", $"节点 '{w.To.NodeId}' 无输入端口 '{w.To.PortName}'。", w.To.NodeId));
        }

        // 类型校验：两端端口都声明了类型且不一致即报错
        foreach (var w in circuit.Wires)
        {
            if (!nodes.TryGetValue(w.From.NodeId, out var f)) continue;
            if (!nodes.TryGetValue(w.To.NodeId, out var t)) continue;
            var ft = f.OutPorts.FirstOrDefault(p => p.Name == w.From.PortName)?.DataType;
            var tt = t.InPorts.FirstOrDefault(p => p.Name == w.To.PortName)?.DataType;
            if (!string.IsNullOrWhiteSpace(ft) && !string.IsNullOrWhiteSpace(tt) && !string.Equals(ft, tt, StringComparison.Ordinal))
                d.Add(new(DiagnosticSeverity.Error, "CFG-007",
                    $"端口类型不匹配：'{w.From}'({ft}) -> '{w.To}'({tt})。", w.To.NodeId));
        }
    }

    private static void CheckFanIn(Circuit circuit, List<CircuitDiagnostic> d)
    {
        // 一个输入端口只允许一条入线；多入须经 Fold/Merge/Join 符号。
        var covered = new HashSet<string>();
        foreach (var s in circuit.Symbols)
        {
            switch (s)
            {
                case FoldSymbol q: covered.Add($"{q.TargetNodeId}:{q.Port}"); break;
                case MergeSymbol m: foreach (var n in new[] { m.NodeId }) _ = n; break;
            }
        }
        var mergeNodes = new HashSet<string>(
            circuit.Symbols.OfType<MergeSymbol>().Select(m => m.NodeId).Concat(
            circuit.Symbols.OfType<JoinSymbol>().Select(j => j.TargetNodeId)));
        var fanIn = circuit.Wires.GroupBy(w => $"{w.To.NodeId}:{w.To.PortName}");
        foreach (var g in fanIn)
        {
            if (g.Count() <= 1) continue;
            var (node, port) = (g.First().To.NodeId, g.First().To.PortName);
            if (covered.Contains(g.Key) || mergeNodes.Contains(node)) continue;
            d.Add(new(DiagnosticSeverity.Error, "CFG-008",
                $"端口 '{node}.{port}' 存在 {g.Count()} 条入线（fan-in），须经 Merge/Join/Fold 显式汇合。", node));
        }
    }

    private static void CheckRecursion(Circuit circuit, List<CircuitDiagnostic> d)
    {
        foreach (var r in circuit.Symbols.OfType<RecursionSymbol>())
        {
            // BaseCase 为必需字段（构造层已强制），此处兜底校验防构造绕过。
            if (r.BaseCase is null)
                d.Add(new(DiagnosticSeverity.Error, "CFG-009", $"Recursion 符号缺少 BaseCase（无限递归无终止声明）。", r.NodeId));
            var node = circuit.Nodes.FirstOrDefault(n => n.Id == r.NodeId);
            if (node is null)
                d.Add(new(DiagnosticSeverity.Error, "CFG-010", $"Recursion 承载节点 '{r.NodeId}' 不存在。", r.NodeId));
            else
            {
                if (node.OutPorts.All(p => p.Name != r.BackEdge.PortName))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-011", $"Recursion 回边源端口 '{r.BackEdge.PortName}' 不存在于节点 '{r.NodeId}'。", r.NodeId));
                if (node.InPorts.All(p => p.Name != r.LoopBack.PortName))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-012", $"Recursion 回连端口 '{r.LoopBack.PortName}' 不存在于节点 '{r.NodeId}'。", r.NodeId));
            }
        }
    }

    private static void CheckCallTargets(Circuit circuit, List<CircuitDiagnostic> d)
    {
        foreach (var c in circuit.Symbols.OfType<CallSymbol>())
        {
            if (string.IsNullOrWhiteSpace(c.Target.CircuitId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-013", $"Call 符号（{c.CallerNodeId}）目标电路为空。", c.CallerNodeId));
            if (circuit.Nodes.All(n => n.Id != c.CallerNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-014", $"Call 调用者节点 '{c.CallerNodeId}' 不存在（孤立引用）。", c.CallerNodeId));
        }
    }
}
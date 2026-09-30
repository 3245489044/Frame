using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 编译期图校验：环检测 / 类型校验 / 端口解析 / 孤立引用 / BaseCase 强制。
/// 非法图直接编译失败，不进入执行。
/// <para>出口投影（CFG-015/016/017）、数据流符号目标端口（CFG-018）与声明-执行对账（CFG-101，Warning）同在编译期判明，
/// 使「编译绿 ⇒ 运行期不再有投影类错误」（原出口解析在运行期抛）。</para>
/// </summary>
public static class CircuitValidator
{
    public static IReadOnlyList<CircuitDiagnostic> Validate(Circuit circuit)
    {
        var d = new List<CircuitDiagnostic>();
        // 重复 Id 由 CheckUniqueNodeIds 判红（CFG-001）：此处不可用 ToDictionary（重复键会先抛，使该判据不可达）。
        var nodes = new Dictionary<string, CircuitNode>(StringComparer.Ordinal);
        foreach (var n in circuit.Nodes) nodes[n.Id] = n;

        CheckUniqueNodeIds(circuit, d);
        CheckCycles(circuit, d);
        CheckPortResolution(circuit, nodes, d); // 环与端口/类型错误并行产出，不因环存在而短路
        CheckFanIn(circuit, d);
        CheckRecursion(circuit, d);
        CheckCallTargets(circuit, d);
        CheckExitSymbols(circuit, nodes, d);
        CheckDataflowTargets(circuit, nodes, d);
        CheckExecutionCoverage(circuit, d);
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

    private static void CheckCycles(Circuit circuit, List<CircuitDiagnostic> d)
    {
        var cycles = CycleDetector.FindCycles(circuit);
        foreach (var cyc in cycles)
            d.Add(new(DiagnosticSeverity.Error, "CFG-002",
                $"Wire 依赖图存在非法环（环只允许由 Recursion 符号产生，不得以 Wire 成环）：{string.Join(" -> ", cyc)}。",
                cyc.FirstOrDefault()));
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

    /// <summary>出口投影校验：格式 / 节点存在性 / 端口存在性（Error）与重复声明（Warning）。</summary>
    private static void CheckExitSymbols(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exit in circuit.Symbols.OfType<ExitSymbol>())
        {
            if (!seen.Add(exit.Port))
                d.Add(new(DiagnosticSeverity.Warning, "CFG-017",
                    $"Exit 端口 '{exit.Port}' 重复声明（收集期去重，只取一次值）。", exit.Port));

            var parts = exit.Port.Split('.', 2);
            var nodeId = parts[0].Trim();
            var portName = parts.Length == 2 ? parts[1].Trim() : string.Empty;
            if (parts.Length != 2 || nodeId.Length == 0 || portName.Length == 0)
            {
                d.Add(new(DiagnosticSeverity.Error, "CFG-015",
                    $"Exit 端口 '{exit.Port}' 格式非法（须为 \"NodeId.Port\"）。", exit.Port));
                continue;
            }
            if (!nodes.TryGetValue(nodeId, out var node))
            {
                d.Add(new(DiagnosticSeverity.Error, "CFG-015",
                    $"Exit 端口 '{exit.Port}' 引用不存在的节点 '{nodeId}'（Exit 须用 \"NodeId.Port\" 格式）。", exit.Port));
                continue;
            }
            if (node.OutPorts.All(p => p.Name != portName))
                d.Add(new(DiagnosticSeverity.Error, "CFG-016",
                    $"Exit 端口 '{exit.Port}' 引用不存在于节点 '{nodeId}' 的输出端口 '{portName}'。", nodeId));
        }
    }

    /// <summary>数据流符号目标校验：Literal/Param/Fold/Unfold/Projection 的目标节点与入端口必须存在。</summary>
    private static void CheckDataflowTargets(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        void Check(string nodeId, string port, string kind)
        {
            if (!nodes.TryGetValue(nodeId, out var node))
            {
                d.Add(new(DiagnosticSeverity.Error, "CFG-018", $"{kind} 符号指向不存在的节点 '{nodeId}'。", nodeId));
                return;
            }
            if (node.InPorts.All(p => p.Name != port))
                d.Add(new(DiagnosticSeverity.Error, "CFG-018",
                    $"{kind} 符号指向不存在于节点 '{nodeId}' 的输入端口 '{port}'。", nodeId));
        }

        foreach (var symbol in circuit.Symbols)
        {
            switch (symbol)
            {
                case ParamSymbol p: Check(p.TargetNodeId, p.Port, nameof(ParamSymbol)); break;
                case LiteralSymbol l: Check(l.TargetNodeId, l.Port, nameof(LiteralSymbol)); break;
                case FoldSymbol f: Check(f.TargetNodeId, f.Port, nameof(FoldSymbol)); break;
                case ProjectionSymbol pr: Check(pr.TargetNodeId, pr.Port, nameof(ProjectionSymbol)); break;
                case UnfoldSymbol u:
                    foreach (var port in u.Ports) Check(u.TargetNodeId, port, nameof(UnfoldSymbol));
                    break;
            }
        }
    }

    /// <summary>
    /// 声明-执行对账（Warning）：Entry/Exit 之外的符号由执行后端（Wire + 入口 + 出口投影）判读，
    /// 其余语义（分支 / 容错 / 循环递归 / 数据流常量 / 子电路 / 引用）目前仅参与编译校验与静态分析。
    /// 可见化「图语义 ≠ 执行语义」的缺口，杜绝「图是对的」被误读为「跑的就是图」。
    /// </summary>
    private static void CheckExecutionCoverage(Circuit circuit, List<CircuitDiagnostic> d)
    {
        var declared = circuit.Symbols
            .Where(s => s.Kind is not (CircuitSymbolKind.Entry or CircuitSymbolKind.Exit))
            .GroupBy(s => s.Kind)
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .ToList();
        if (declared.Count == 0) return;

        d.Add(new(DiagnosticSeverity.Warning, "CFG-101",
            "以下符号已声明但执行后端未消费（仅参与编译校验/静态分析）："
            + string.Join(" | ", declared.Select(g => $"{g.Key}×{g.Count()}")) + "。"));
    }
}
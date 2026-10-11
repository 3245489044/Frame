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
        CheckControlEdges(circuit, nodes, d);
        CheckFailurePolicies(circuit, nodes, d);
        CheckBranches(circuit, nodes, d);
        CheckCodeRefs(circuit, d);
        CheckEffects(circuit, d);
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

    /// <summary>
    /// 受控边判定键校验（CFG-025，G3）：判定载体须可解析 —— 节点端口键（NodeId.Port 存在）/ 入口端口（已声明）。
    /// 使「编译绿 ⇒ 运行期不再有判定载体类错误」，不留静默失效的门控。
    /// </summary>
    private static void CheckControlEdges(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        var entries = new HashSet<string>(
            circuit.Symbols.OfType<EntrySymbol>().Select(e => e.Port), StringComparer.Ordinal);

        foreach (var wire in circuit.Wires)
        {
            if (string.IsNullOrWhiteSpace(wire.When)) continue;
            var when = wire.When!;

            if (when.StartsWith('$'))
            {
                // $ 前缀 = 上下文键（执行端 ResolveJudgeKey 原样保留），与分支侧 CheckJudgeKeyResolvable（CFG-026）同口径：
                // 任意 $键 放行，交执行期「缺失即不激活」；仅 $entry.X 须校验入口已声明。
                var body = when[1..];
                if (body.StartsWith("entry.", StringComparison.Ordinal)
                    && !entries.Contains(body["entry.".Length..]))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-025",
                        $"受控边判定键 '{when}' 引用了未声明的入口端口。", wire.To.NodeId));
            }
            else if (when.Contains('.'))
            {
                CheckJudgeNodePort(when, wire, nodes, d);
            }
            else if (!entries.Contains(when))
            {
                d.Add(new(DiagnosticSeverity.Error, "CFG-025",
                    $"受控边判定端口 '{when}' 既非入口端口也非节点端口键（判定载体不可解析）。", wire.To.NodeId));
            }
        }
    }

    private static void CheckJudgeNodePort(
        string nodePort, CircuitWire wire, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        var parts = nodePort.Split('.', 2);
        if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
        {
            d.Add(new(DiagnosticSeverity.Error, "CFG-025",
                $"受控边判定键 '{nodePort}' 格式非法（须为 \"NodeId.Port\"）。", wire.To.NodeId));
            return;
        }

        var nodeId = parts[0].Trim();
        var portName = parts[1].Trim();
        if (!nodes.TryGetValue(nodeId, out var judgeNode))
        {
            d.Add(new(DiagnosticSeverity.Error, "CFG-025",
                $"受控边判定键 '{nodePort}' 引用不存在的节点 '{nodeId}'。", wire.To.NodeId));
            return;
        }
        if (judgeNode.OutPorts.All(p => p.Name != portName))
            d.Add(new(DiagnosticSeverity.Error, "CFG-025",
                $"受控边判定键 '{nodePort}' 引用的输出端口 '{portName}' 不存在于节点 '{nodeId}'。", wire.To.NodeId));
    }

    /// <summary>容错策略目标校验（CFG-024，G4）：Retry/Fallback/CatchRoute/TryGuard 指向的节点必须存在（编译期判红）。</summary>
    private static void CheckFailurePolicies(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        foreach (var retry in circuit.Symbols.OfType<RetrySymbol>())
            if (!nodes.ContainsKey(retry.NodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-024", $"Retry 承载节点 '{retry.NodeId}' 不存在。", retry.NodeId));

        foreach (var fallback in circuit.Symbols.OfType<FallbackSymbol>())
        {
            if (!nodes.ContainsKey(fallback.PrimaryNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-024",
                    $"Fallback 主路径节点 '{fallback.PrimaryNodeId}' 不存在。", fallback.PrimaryNodeId));
            if (!nodes.ContainsKey(fallback.AlternativeNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-024",
                    $"Fallback 备选节点 '{fallback.AlternativeNodeId}' 不存在（失败路由无落点）。", fallback.PrimaryNodeId));
        }

        foreach (var catchRoute in circuit.Symbols.OfType<CatchRouteSymbol>())
        {
            if (!nodes.ContainsKey(catchRoute.NodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-024",
                    $"CatchRoute 承载节点 '{catchRoute.NodeId}' 不存在。", catchRoute.NodeId));
            if (!nodes.ContainsKey(catchRoute.RemedyNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-024",
                    $"CatchRoute 补救节点 '{catchRoute.RemedyNodeId}' 不存在（失败路由无落点）。", catchRoute.NodeId));
        }

        foreach (var guard in circuit.Symbols.OfType<TryGuardSymbol>())
            foreach (var protectedId in guard.ProtectedNodeIds)
                if (!nodes.ContainsKey(protectedId))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-024",
                        $"TryGuard 保护节点 '{protectedId}' 不存在。", protectedId));
    }

    /// <summary>
    /// 分支 / 分派声明校验（CFG-026，R1）：判定键/选择键可解析（复用 CFG-025 口径）+ 目标节点存在 + then/else 互斥。
    /// 使「编译绿 ⇒ 运行期不再有分支路由类错误」，不留静默失效的路由。
    /// </summary>
    private static void CheckBranches(Circuit circuit, IReadOnlyDictionary<string, CircuitNode> nodes, List<CircuitDiagnostic> d)
    {
        var entries = new HashSet<string>(
            circuit.Symbols.OfType<EntrySymbol>().Select(e => e.Port), StringComparer.Ordinal);

        foreach (var branch in circuit.Symbols.OfType<BranchSymbol>())
        {
            if (string.IsNullOrWhiteSpace(branch.JudgeKey))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"Branch 符号（{branch.ConditionNodeId}）缺少判定键（执行面不可门控）。", branch.ConditionNodeId));
            else
                CheckJudgeKeyResolvable(branch.JudgeKey!, branch.ConditionNodeId, nodes, entries, d);

            if (!nodes.ContainsKey(branch.ThenNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"Branch then 目标 '{branch.ThenNodeId}' 不存在。", branch.ThenNodeId));

            if (!string.IsNullOrWhiteSpace(branch.ElseNodeId))
            {
                if (!nodes.ContainsKey(branch.ElseNodeId!))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                        $"Branch else 目标 '{branch.ElseNodeId}' 不存在。", branch.ElseNodeId));
                if (string.Equals(branch.ElseNodeId, branch.ThenNodeId, StringComparison.Ordinal))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                        $"Branch then/else 目标不可为同一节点 '{branch.ThenNodeId}'（互斥分支）。", branch.ThenNodeId));
            }
        }

        foreach (var sw in circuit.Symbols.OfType<SwitchSymbol>())
        {
            if (string.IsNullOrWhiteSpace(sw.SelectorKey))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"Switch 符号（{sw.SelectorNodeId}）缺少选择键（执行面不可路由）。", sw.SelectorNodeId));
            else
                CheckJudgeKeyResolvable(sw.SelectorKey!, sw.SelectorNodeId, nodes, entries, d);

            if (sw.Routes.Count == 0 && string.IsNullOrWhiteSpace(sw.DefaultNodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"Switch 符号（{sw.SelectorNodeId}）无任何路由（至少一条 route 或 default）。", sw.SelectorNodeId));

            foreach (var kv in sw.Routes)
                if (!nodes.ContainsKey(kv.Value))
                    d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                        $"Switch 路由 '{kv.Key}' 目标 '{kv.Value}' 不存在。", kv.Value));
            if (!string.IsNullOrWhiteSpace(sw.DefaultNodeId) && !nodes.ContainsKey(sw.DefaultNodeId!))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"Switch 默认目标 '{sw.DefaultNodeId}' 不存在。", sw.DefaultNodeId));
        }
    }

    /// <summary>判定/选择键可解析（CFG-026，口径同 CFG-025）：$ 前缀原样 / 含点 ⇒ 节点端口键 / 否则 ⇒ 入口端口。</summary>
    private static void CheckJudgeKeyResolvable(
        string key, string ownerNodeId, IReadOnlyDictionary<string, CircuitNode> nodes,
        HashSet<string> entries, List<CircuitDiagnostic> d)
    {
        if (key.StartsWith('$')) return; // 原样上下文键（装配侧供给；执行期缺失 ⇒ 不激活）

        if (key.Contains('.'))
        {
            var parts = key.Split('.', 2);
            var nodeId = parts[0].Trim();
            var portName = parts[1].Trim();
            if (parts.Length != 2 || nodeId.Length == 0 || portName.Length == 0)
            {
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"分支判定键 '{key}' 格式非法（须为 \"NodeId.Port\"）。", ownerNodeId));
                return;
            }
            if (!nodes.TryGetValue(nodeId, out var judgeNode))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"分支判定键 '{key}' 引用不存在的节点 '{nodeId}'。", ownerNodeId));
            else if (judgeNode.OutPorts.All(p => p.Name != portName))
                d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                    $"分支判定键 '{key}' 引用的输出端口 '{portName}' 不存在于节点 '{nodeId}'。", ownerNodeId));
            return;
        }

        if (!entries.Contains(key))
            d.Add(new(DiagnosticSeverity.Error, "CFG-026",
                $"分支判定键 '{key}' 既非入口端口也非节点端口键（判定载体不可解析）。", ownerNodeId));
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
    /// 代码实体引用校验（CFG-040，G2/G6）：绑定了 <see cref="Core.CodeRef"/> 的节点，其类型/方法名须非空、
    /// 端口类型表须指向真实端口；若外部注册了 <see cref="CodeEntityResolver.Current"/>，则校验实体可解析
    /// （与真实代码对拍，替代手工/自动之分）。
    /// </summary>
    private static void CheckCodeRefs(Circuit circuit, List<CircuitDiagnostic> d)
    {
        foreach (var node in circuit.Nodes)
        {
            if (node.Code is null) continue;

            if (!node.Code.IsResolvable)
                d.Add(new(DiagnosticSeverity.Error, "CFG-040",
                    $"节点 '{node.Id}' 的代码实体引用不完整（类型/方法名须非空）：'{node.Code}'。", node.Id));

            if (node.Code.PortTypes is { Count: > 0 } types)
            {
                foreach (var key in types.Keys)
                {
                    if (node.InPorts.All(p => p.Name != key) && node.OutPorts.All(p => p.Name != key))
                        d.Add(new(DiagnosticSeverity.Error, "CFG-040",
                            $"节点 '{node.Id}' 的代码引用端口类型表含未知端口 '{key}'。", node.Id));
                }
            }

            if (node.Code.IsResolvable && CodeEntityResolver.Current is { } resolver && !resolver(node.Code))
                d.Add(new(DiagnosticSeverity.Error, "CFG-040",
                    $"节点 '{node.Id}' 的代码实体 '{node.Code.DisplayName}' 不可解析（与真实代码对拍失败）。", node.Id));
        }
    }

    /// <summary>
    /// 效果符号校验（CFG-042，G7）：承载节点须存在；通道 / 主题须非空（判据载体 = 结构事实）。
    /// </summary>
    private static void CheckEffects(Circuit circuit, List<CircuitDiagnostic> d)
    {
        foreach (var e in circuit.Symbols.OfType<EffectSymbol>())
        {
            if (circuit.Nodes.All(n => n.Id != e.NodeId))
                d.Add(new(DiagnosticSeverity.Error, "CFG-042",
                    $"Effect 符号承载节点 '{e.NodeId}' 不存在。", e.NodeId));

            if (string.IsNullOrWhiteSpace(e.Channel) || string.IsNullOrWhiteSpace(e.Subject))
                d.Add(new(DiagnosticSeverity.Error, "CFG-042",
                    $"Effect 符号（{e.NodeId}）的通道 / 主题不可为空（判据载体须为结构事实）。", e.NodeId));
        }
    }

    /// <summary>
    /// 声明-执行对账（Warning）：只报**能力表之外**的符号（判据单源 = <see cref="CircuitExecutionCapabilities"/>）。
    /// 表内 = 执行后端已消费（Entry / Exit / Wire + 数据流符号）；表外语义（分支 / 容错 / 循环递归 / 子电路 / 引用）
    /// 目前仅参与编译校验与静态分析 —— 可见化「图语义 ≠ 执行语义」的缺口，杜绝「图是对的」被误读为「跑的就是图」。
    /// </summary>
    private static void CheckExecutionCoverage(Circuit circuit, List<CircuitDiagnostic> d)
    {
        var declared = circuit.Symbols
            .Where(s => !CircuitExecutionCapabilities.IsConsumed(s.Kind))
            .GroupBy(s => s.Kind)
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .ToList();
        if (declared.Count == 0) return;

        d.Add(new(DiagnosticSeverity.Warning, "CFG-101",
            "以下符号已声明但执行后端未消费（仅参与编译校验/静态分析）："
            + string.Join(" | ", declared.Select(g => $"{g.Key}×{g.Count()}")) + "。"));
    }
}
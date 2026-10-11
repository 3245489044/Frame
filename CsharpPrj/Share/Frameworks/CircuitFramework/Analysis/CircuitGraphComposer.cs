using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Analysis;

/// <summary>图组合结果：根图（各命名电路 ∪ 成一张连通图）+ 未解析的跨电路目标。</summary>
public sealed record CircuitCompositionResult(Circuit Root, IReadOnlyList<string> UnresolvedTargets);

/// <summary>
/// 层级组合原语（G4）：把一组命名电路（段配方 / 会话域 / 子电路）**∪ 成一张连通根图** ——
/// 节点以「电路 Id / 节点 Id」限定名去重，跨电路 <c>Call</c>/<c>SubCircuit</c> 保留为边并校验目标可解析。
/// <para>
/// 用途：使「函数流动图」不再止于单段/单电路 —— A–F 段 + 会话 + 各域子电路经此缝成**一张图**，
/// 交 <see cref="ControlFlowGraph"/> / <see cref="CircuitFlowRenderer"/> 渲染（macro 全景 / micro 放大）。
/// </para>
/// <para>纯读投影、不回写业务；框架零领域类型。</para>
/// </summary>
public static class CircuitGraphComposer
{
    /// <summary>限定名分隔符（电路 Id / 节点 Id）。</summary>
    public const string NodeSeparator = "/";

    /// <summary>组合根图 Id（缺省；供渲染命名）。</summary>
    public const string RootCircuitId = "circuit.composition.root";

    /// <summary>限定节点 Id（<c>{circuitId}/{nodeId}</c>）。</summary>
    public static string Qualify(string circuitId, string nodeId) => circuitId + NodeSeparator + nodeId;

    /// <summary>
    /// 把一组电路 ∪ 成根图。节点/连线以限定名并入；跨电路调用边（Call/SubCircuit）保留并记录未解析目标。
    /// </summary>
    /// <param name="circuits">待组合的命名电路集合（Id 唯一；重复 Id 后者覆盖前者）。</param>
    public static CircuitCompositionResult Compose(IEnumerable<Circuit> circuits)
    {
        ArgumentNullException.ThrowIfNull(circuits);

        var list = circuits.Where(c => c is not null).ToList();
        var byId = new Dictionary<string, Circuit>(StringComparer.Ordinal);
        foreach (var c in list)
            if (!string.IsNullOrWhiteSpace(c.Id)) byId[c.Id] = c;

        var root = new Circuit(RootCircuitId);
        var unresolved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in list)
        {
            foreach (var n in c.Nodes)
            {
                var node = root.AddNode(Qualify(c.Id, n.Id), n.Kind);
                if (n.Code is not null) node.Code = n.Code;
                foreach (var p in n.InPorts) node.AddIn(p.Name, p.DataType);
                foreach (var p in n.OutPorts) node.AddOut(p.Name, p.DataType);
            }

            foreach (var w in c.Wires)
                root.Wires.Add(new CircuitWire(Qualify(c.Id, w.From.NodeId), w.From.PortName,
                    Qualify(c.Id, w.To.NodeId), w.To.PortName) { When = w.When });

            // 域内顺序 / 效果符号（限定承载节点）：使根图保留无数据键依赖。
            foreach (var s in c.Symbols.OfType<SequenceSymbol>())
                root.Append(new SequenceSymbol { NodeIds = s.NodeIds.Select(id => Qualify(c.Id, id)).ToArray() });
            foreach (var e in c.Symbols.OfType<EffectSymbol>())
                root.Append(new EffectSymbol
                {
                    NodeId = Qualify(c.Id, e.NodeId),
                    Effect = e.Effect,
                    Channel = e.Channel,
                    Subject = e.Subject,
                });
        }

        // 跨电路调用边：Call/SubCircuit → 指向目标电路（根图上可见 + 目标可解析校验）。
        foreach (var c in list)
        {
            foreach (var call in c.Symbols.OfType<CallSymbol>())
            {
                if (!byId.ContainsKey(call.Target.CircuitId)) unresolved.Add(call.Target.CanonicalId);
                root.Append(new CallSymbol { CallerNodeId = Qualify(c.Id, call.CallerNodeId), Target = call.Target });
            }

            foreach (var sub in c.Symbols.OfType<SubCircuitSymbol>())
            {
                if (!byId.ContainsKey(sub.Target.CircuitId)) unresolved.Add(sub.Target.CanonicalId);
                root.Append(new SubCircuitSymbol { NodeId = Qualify(c.Id, sub.NodeId), Target = sub.Target });
            }
        }

        return new CircuitCompositionResult(
            root,
            unresolved.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>组合并投影为全流程图（一张连通图；macro/micro 同图）。</summary>
    public static ControlFlowGraph ComposeFlow(IEnumerable<Circuit> circuits) => new(Compose(circuits).Root);

    /// <summary>跨电路目标不可解析校验（CFG-041）：Call/SubCircuit 目标须在组合集合内。</summary>
    public static IReadOnlyList<CircuitDiagnostic> ValidateResolution(IEnumerable<Circuit> circuits)
    {
        var result = Compose(circuits);
        return result.UnresolvedTargets
            .Select(t => new CircuitDiagnostic(DiagnosticSeverity.Error, "CFG-041",
                $"跨电路调用 / 子电路目标 '{t}' 不可解析（图组合集合内缺该电路）。"))
            .ToList();
    }
}

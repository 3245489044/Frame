using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 一条输入端口绑定：目标端口名 ← 上下文键（源产物 "$NodeId.Port" 或入口 "$entry.Port"）。
/// 编译期派生、执行期查表，取代执行期逐节点重扫 Wires / EntrySymbol。
/// </summary>
public readonly record struct InputBinding(string Port, string SourceKey)
{
    /// <summary>Wire 绑定：上游产物 → 下游入端口（键 = 源产物键）。</summary>
    public static InputBinding FromWire(string toPort, string fromNodeId, string fromPort)
        => new(toPort, $"${fromNodeId}.{fromPort}");

    /// <summary>入口绑定：外部值 → 同名入端口（键 = 入口供给键）。</summary>
    public static InputBinding FromEntry(string port) => new(port, $"$entry.{port}");
}

/// <summary>出口引用：声明面端口名（"NodeId.Port"）→ 节点/端口（存在性由编译期校验判红）。</summary>
public readonly record struct ExitRef(string Port, string NodeId, string PortName);

/// <summary>
/// 编译期运行时索引（执行期零重建）：节点查找 + 每节点入边绑定 + 入口端口集 + 出口清单。
/// 由 <see cref="ExecutionPlan.Index"/> 承载（<see cref="CircuitCompiler"/> 产出）；
/// 手工构造的计划（无索引）由执行层即时 <see cref="Build"/> 一份，保证两条路径语义一致。
/// </summary>
public sealed class CircuitRuntimeIndex
{
    private readonly Dictionary<string, CircuitNode> _nodesById;
    private readonly Dictionary<string, IReadOnlyList<InputBinding>> _inputsByNode;
    private readonly List<string> _entryPorts;
    private readonly List<ExitRef> _exits;

    private CircuitRuntimeIndex(
        Dictionary<string, CircuitNode> nodesById,
        Dictionary<string, IReadOnlyList<InputBinding>> inputsByNode,
        List<string> entryPorts,
        List<ExitRef> exits)
    {
        _nodesById = nodesById;
        _inputsByNode = inputsByNode;
        _entryPorts = entryPorts;
        _exits = exits;
    }

    /// <summary>节点 Id → 节点（O(1) 查找，取代执行期线性查找）。</summary>
    public IReadOnlyDictionary<string, CircuitNode> NodesById => _nodesById;

    /// <summary>入口端口清单（注册序，去重）。</summary>
    public IReadOnlyList<string> EntryPorts => _entryPorts;

    /// <summary>出口清单（声明序）。</summary>
    public IReadOnlyList<ExitRef> Exits => _exits;

    /// <summary>某节点的入端口绑定集（无绑定 = 空集，不返回 null）。</summary>
    public IReadOnlyList<InputBinding> InputsOf(string nodeId)
        => _inputsByNode.TryGetValue(nodeId, out var list) ? list : Array.Empty<InputBinding>();

    /// <summary>按电路装配索引（O(N+W) 一次）：Wire 绑定 → 入口绑定（后者覆盖前者，与既有供给优先级一致）→ 出口清单。</summary>
    public static CircuitRuntimeIndex Build(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var nodesById = new Dictionary<string, CircuitNode>(StringComparer.Ordinal);
        foreach (var node in circuit.Nodes) nodesById[node.Id] = node; // 重复 Id 由校验器判红（CFG-001），此处后写覆盖

        var entryPorts = new List<string>();
        var entrySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in circuit.Symbols.OfType<EntrySymbol>())
            if (entrySet.Add(entry.Port)) entryPorts.Add(entry.Port);

        var bindings = new Dictionary<string, List<InputBinding>>(StringComparer.Ordinal);

        // ① Wire 绑定（上游产物 → 下游入端口）
        foreach (var wire in circuit.Wires)
        {
            if (!bindings.TryGetValue(wire.To.NodeId, out var list))
                bindings[wire.To.NodeId] = list = new List<InputBinding>(2);
            list.Add(InputBinding.FromWire(wire.To.PortName, wire.From.NodeId, wire.From.PortName));
        }

        // ② 入口绑定（仅声明了同名入端口的节点；后追加 ⇒ 与既有「入口覆盖 Wire」优先级一致）
        foreach (var node in circuit.Nodes)
        {
            foreach (var port in node.InPorts)
            {
                if (!entrySet.Contains(port.Name)) continue;
                if (!bindings.TryGetValue(node.Id, out var list))
                    bindings[node.Id] = list = new List<InputBinding>(2);
                list.Add(InputBinding.FromEntry(port.Name));
            }
        }

        // ③ 出口清单（格式/存在性由编译期校验 CFG-015/016 判红；此处按 "NodeId.Port" 拆分）
        var exits = new List<ExitRef>();
        foreach (var exit in circuit.Symbols.OfType<ExitSymbol>())
        {
            var parts = exit.Port.Split('.', 2);
            var nodeId = parts[0].Trim();
            var portName = parts.Length == 2 ? parts[1].Trim() : string.Empty;
            exits.Add(new ExitRef(exit.Port, nodeId, portName));
        }

        var frozen = new Dictionary<string, IReadOnlyList<InputBinding>>(bindings.Count, StringComparer.Ordinal);
        foreach (var pair in bindings) frozen[pair.Key] = pair.Value;

        return new CircuitRuntimeIndex(nodesById, frozen, entryPorts, exits);
    }
}

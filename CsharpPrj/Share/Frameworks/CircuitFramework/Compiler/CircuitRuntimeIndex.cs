using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Compiler;

/// <summary>输入端口绑定的取值方式（一条绑定 = 目标端口 + 取值方式 + 载荷）。</summary>
public enum InputBindingKind
{
    /// <summary>上游节点产物（键 = <c>"$FromNodeId.FromPort"</c>）。</summary>
    Wire,

    /// <summary>电路入口供给（键 = <c>"$entry.Port"</c>）。</summary>
    Entry,

    /// <summary>常量注入（值 = <see cref="InputBinding.Payload"/>；不查上下文）。</summary>
    Literal,

    /// <summary>端口具名绑定（Source 为 "node.port" ⇒ 上游产物键；否则 ⇒ 外部值名入口键）。</summary>
    Param,

    /// <summary>字段路径取值（源键 = <see cref="InputBinding.SourceKey"/>，路径 = <see cref="InputBinding.Payload"/>）。</summary>
    Projection,

    /// <summary>多路聚合为数组（源键集 = <see cref="InputBinding.SourceKeys"/>）。</summary>
    Fold,

    /// <summary>一路扇出（源键 = <see cref="InputBinding.SourceKey"/>，子端口名 = <see cref="InputBinding.Payload"/>）。</summary>
    Unfold,
}

/// <summary>
/// 一条输入端口绑定：目标端口名 ← 取值方式（编译期派生、执行期查表），
/// 取代执行期逐节点重扫 Wires / EntrySymbol / 数据流符号。
/// <para>同名端口的优先级 = 装配序（<see cref="CircuitRuntimeIndex.Build"/> 中后追加者覆盖前者）：
/// Wire/Param → Entry → 数据流符号（显式声明优先于隐式同名供给）。</para>
/// </summary>
public readonly record struct InputBinding(
    string Port,
    InputBindingKind Kind,
    string SourceKey,
    object? Payload = null,
    IReadOnlyList<string>? SourceKeys = null)
{
    /// <summary>Wire 绑定：上游产物 → 下游入端口（键 = 源产物键）。</summary>
    public static InputBinding FromWire(string toPort, string fromNodeId, string fromPort)
        => new(toPort, InputBindingKind.Wire, $"${fromNodeId}.{fromPort}");

    /// <summary>入口绑定：外部值 → 同名入端口（键 = 入口供给键）。</summary>
    public static InputBinding FromEntry(string port) => new(port, InputBindingKind.Entry, $"$entry.{port}");

    /// <summary>常量注入绑定（Payload = 常量值）。</summary>
    public static InputBinding FromLiteral(string port, object? value)
        => new(port, InputBindingKind.Literal, string.Empty, value);

    /// <summary>具名绑定（<paramref name="sourceKey"/> 已是上下文键，如 <c>"$a.p"</c> 或 <c>"$entry.seed"</c>）。</summary>
    public static InputBinding FromParam(string port, string sourceKey)
        => new(port, InputBindingKind.Param, sourceKey);

    /// <summary>字段路径取值绑定（<paramref name="fieldPath"/> 为点链，如 <c>"user.address.city"</c>）。</summary>
    public static InputBinding FromProjection(string port, string sourceKey, string fieldPath)
        => new(port, InputBindingKind.Projection, sourceKey, fieldPath);

    /// <summary>多路聚合绑定（<paramref name="sourceKeys"/> 为按声明序的源键集）。</summary>
    public static InputBinding FromFold(string port, IReadOnlyList<string> sourceKeys)
        => new(port, InputBindingKind.Fold, string.Empty, null, sourceKeys);

    /// <summary>扇出绑定（<paramref name="subPort"/> 为源产物内取值的子端口名）。</summary>
    public static InputBinding FromUnfold(string port, string sourceKey, string subPort)
        => new(port, InputBindingKind.Unfold, sourceKey, subPort);
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

        // ③ 数据流符号绑定（B3-1）：Literal/Param/Projection/Fold/Unfold 由执行后端消费。
        //    追加在 Wire/Entry 之后 ⇒ 显式声明的数据流符号覆盖同名隐式供给。
        AppendDataflowBindings(circuit, nodesById, bindings);

        // ④ 出口清单（格式/存在性由编译期校验 CFG-015/016 判红；此处按 "NodeId.Port" 拆分）
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

    /// <summary>
    /// 数据流符号 → 输入端口绑定（B3-1）：Literal（常量注入）/ Param（具名绑定）/ Projection（字段路径取值）/
    /// Fold（多路聚合为数组）/ Unfold（一路扇出多端口）。
    /// 目标节点与入端口的存在性由编译期校验 CFG-018 判红（此处按声明直译，不重复判据）。
    /// <para>Param.Source 形如 <c>"node.port"</c> 且该 node 存在 ⇒ 上游产物键；否则视为外部值名 ⇒ 入口键
    /// （外部值须由 <see cref="EntrySymbol"/> 声明后经入口供给，不隐式生效）。</para>
    /// </summary>
    private static void AppendDataflowBindings(
        Circuit circuit,
        IReadOnlyDictionary<string, CircuitNode> nodesById,
        Dictionary<string, List<InputBinding>> bindings)
    {
        void Add(string nodeId, InputBinding binding)
        {
            if (!bindings.TryGetValue(nodeId, out var list))
                bindings[nodeId] = list = new List<InputBinding>(2);
            list.Add(binding);
        }

        static string SourceKeyOf(PortRef source) => $"${source.NodeId}.{source.PortName}";

        foreach (var symbol in circuit.Symbols)
        {
            switch (symbol)
            {
                case LiteralSymbol literal:
                    Add(literal.TargetNodeId, InputBinding.FromLiteral(literal.Port, literal.Value));
                    break;

                case ParamSymbol param:
                    {
                        var parts = param.Source.Split('.', 2);
                        var upstream = parts.Length == 2
                            && nodesById.ContainsKey(parts[0].Trim())
                            && parts[1].Trim().Length > 0;
                        Add(param.TargetNodeId, InputBinding.FromParam(param.Port, upstream
                            ? $"${parts[0].Trim()}.{parts[1].Trim()}"
                            : $"$entry.{param.Source}"));
                        break;
                    }

                case ProjectionSymbol projection:
                    Add(projection.TargetNodeId, InputBinding.FromProjection(
                        projection.Port, SourceKeyOf(projection.Source), projection.FieldPath));
                    break;

                case FoldSymbol fold:
                    Add(fold.TargetNodeId, InputBinding.FromFold(fold.Port, fold.Sources.Select(SourceKeyOf).ToList()));
                    break;

                case UnfoldSymbol unfold:
                    // 一路扇出：为每个目标端口各产一条绑定（执行期按子端口名在源产物内取值）。
                    foreach (var port in unfold.Ports)
                        Add(unfold.TargetNodeId, InputBinding.FromUnfold(port, SourceKeyOf(unfold.Source), port));
                    break;
            }
        }
    }
}

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
/// 路由匹配语义（R1）：判定键取值的匹配方式 —— Truthy（为真激活，G3 缺省）/ Falsy（为假激活）/
/// AnyOf（取值 ∈ <see cref="RouteBinding.MatchValues"/>）/ NoneOf（取值 ∉ MatchValues，供 switch 默认兜底）。
/// </summary>
public enum RouteMatch
{
    /// <summary>判定键为真时激活（G3 缺省；<see cref="RouteBinding.WhenKey"/> 归一规则同 Wire.When）。</summary>
    Truthy,

    /// <summary>判定键为假时激活（分支 else 侧）。</summary>
    Falsy,

    /// <summary>判定键取值的字符串形式 ∈ <see cref="RouteBinding.MatchValues"/> 时激活（switch route）。</summary>
    AnyOf,

    /// <summary>判定键取值的字符串形式 ∉ <see cref="RouteBinding.MatchValues"/> 时激活（switch 默认兜底）。</summary>
    NoneOf,
}

/// <summary>
/// 受控边 / 分支路由绑定（G3+R1）：目标节点受判定键 <see cref="WhenKey"/> 按 <see cref="Match"/> 门控
/// （目标节点全部路由条件成立才执行）。门控只读 <see cref="WhenKey"/>/<see cref="Match"/>/<see cref="MatchValues"/>；
/// <see cref="ToPort"/> 为声明来源端口，仅供追溯/诊断，不参与激活判定
/// （受控边填目标端口名、分支/分派为空串）。
/// </summary>
public readonly record struct RouteBinding(
    string ToPort,
    string WhenKey,
    RouteMatch Match = RouteMatch.Truthy,
    IReadOnlyList<string>? MatchValues = null);

/// <summary>
/// 节点容错策略（G4，机制侧）：重试 / 失败路由 / 受保护。
/// 框架只做机制（次数 / 退避 / 路由），阈值与熔断归宿主（对齐「框架零领域类型」纪律）。
/// </summary>
public sealed record NodeFailurePolicy
{
    /// <summary>最大尝试次数（含首次；&lt;= 1 = 不重试）。</summary>
    public int RetryMaxAttempts { get; init; }

    /// <summary>指数退避基底（毫秒；0 = 不退避）。第 n 次重试退避 = base × 2^(n-1)。</summary>
    public double RetryBackoffBaseMs { get; init; }

    /// <summary>备选节点 Id（主路径失败 → 备选，机制侧）。</summary>
    public string? FallbackNodeId { get; init; }

    /// <summary>补救节点 Id（失败后路由，机制侧）。</summary>
    public string? CatchRouteNodeId { get; init; }

    /// <summary>是否受保护（TryGuard：失败吞并继续，不向执行序上抛）。</summary>
    public bool Guarded { get; init; }

    /// <summary>无策略（默认）。</summary>
    public static readonly NodeFailurePolicy None = new();

    /// <summary>是否配置了任一机制（供执行层跳过空策略分支）。</summary>
    public bool HasAny => RetryMaxAttempts > 1 || RetryBackoffBaseMs > 0
        || FallbackNodeId is not null || CatchRouteNodeId is not null || Guarded;
}

/// <summary>
/// 编译期运行时索引（执行期零重建）：节点查找 + 每节点入边绑定 + 入口端口集 + 出口清单。
/// 由 <see cref="ExecutionPlan.Index"/> 承载（<see cref="CircuitCompiler"/> 产出）；
/// 手工构造的计划（无索引）由执行层即时 <see cref="Build"/> 一份，保证两条路径语义一致。
/// </summary>
public sealed class CircuitRuntimeIndex
{
    private readonly Dictionary<string, CircuitNode> _nodesById;
    private readonly Dictionary<string, IReadOnlyList<InputBinding>> _inputsByNode;
    private readonly Dictionary<string, IReadOnlyList<RouteBinding>> _routesByNode;
    private readonly Dictionary<string, NodeFailurePolicy> _failureByNode;
    private readonly List<string> _entryPorts;
    private readonly List<ExitRef> _exits;

    private CircuitRuntimeIndex(
        Dictionary<string, CircuitNode> nodesById,
        Dictionary<string, IReadOnlyList<InputBinding>> inputsByNode,
        Dictionary<string, IReadOnlyList<RouteBinding>> routesByNode,
        Dictionary<string, NodeFailurePolicy> failureByNode,
        List<string> entryPorts,
        List<ExitRef> exits)
    {
        _nodesById = nodesById;
        _inputsByNode = inputsByNode;
        _routesByNode = routesByNode;
        _failureByNode = failureByNode;
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

    /// <summary>某节点的受控边门控集（无门控 = 空集 ⇒ 无条件执行，G3）。</summary>
    public IReadOnlyList<RouteBinding> RoutesOf(string nodeId)
        => _routesByNode.TryGetValue(nodeId, out var list) ? list : Array.Empty<RouteBinding>();

    /// <summary>某节点的容错策略（无声明 = <see cref="NodeFailurePolicy.None"/>，G4）。</summary>
    public NodeFailurePolicy FailurePolicyOf(string nodeId)
        => _failureByNode.TryGetValue(nodeId, out var policy) ? policy : NodeFailurePolicy.None;

    /// <summary>
    /// 受控边判定键归一（G3）：以 <c>$</c> 开头 ⇒ 原样；含 <c>.</c> ⇒ 节点端口键 <c>$Node.Port</c>；
    /// 否则 ⇒ 入口键 <c>$entry.{when}</c>。判定载体须为结构化数据键，不做表达式求值。
    /// </summary>
    internal static string ResolveJudgeKey(string when)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(when);
        if (when.StartsWith('$')) return when;
        return when.Contains('.') ? "$" + when : "$entry." + when;
    }

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

        // ③.5 受控边（G3）+ 分支/分派（R1）：带 When 的 Wire → Truthy；Branch/Switch → 后继激活/抑制（路由表）
        var routes = new Dictionary<string, List<RouteBinding>>(StringComparer.Ordinal);
        void AddRoute(string targetNodeId, RouteBinding binding)
        {
            if (string.IsNullOrWhiteSpace(targetNodeId)) return;
            if (!routes.TryGetValue(targetNodeId, out var list))
                routes[targetNodeId] = list = new List<RouteBinding>(2);
            list.Add(binding);
        }

        foreach (var wire in circuit.Wires)
        {
            if (string.IsNullOrWhiteSpace(wire.When)) continue;
            AddRoute(wire.To.NodeId, new RouteBinding(wire.To.PortName, ResolveJudgeKey(wire.When!), RouteMatch.Truthy));
        }

        foreach (var symbol in circuit.Symbols)
        {
            switch (symbol)
            {
                // R1 分支：then 真激活 / else 假激活（判定键可为空 ⇒ 仅符号视图，不门控，源兼容）
                case BranchSymbol branch when !string.IsNullOrWhiteSpace(branch.JudgeKey):
                    {
                        var judge = ResolveJudgeKey(branch.JudgeKey!);
                        AddRoute(branch.ThenNodeId, new RouteBinding(string.Empty, judge, RouteMatch.Truthy));
                        if (!string.IsNullOrWhiteSpace(branch.ElseNodeId))
                            AddRoute(branch.ElseNodeId!, new RouteBinding(string.Empty, judge, RouteMatch.Falsy));
                        break;
                    }

                // R1 分派：命中 route 激活（AnyOf）/ 未命中则 default（NoneOf）
                case SwitchSymbol sw when !string.IsNullOrWhiteSpace(sw.SelectorKey):
                    {
                        var selector = ResolveJudgeKey(sw.SelectorKey!);
                        var values = new List<string>(sw.Routes.Count);
                        foreach (var kv in sw.Routes)
                        {
                            values.Add(kv.Key);
                            AddRoute(kv.Value, new RouteBinding(string.Empty, selector, RouteMatch.AnyOf, new[] { kv.Key }));
                        }
                        // 默认兜底须在「无 route 命中」时激活：零 route 时 NoneOf 空集 ⇒ 恒激活（仅-default 分派合法，不得静默失效）。
                        if (!string.IsNullOrWhiteSpace(sw.DefaultNodeId))
                            AddRoute(sw.DefaultNodeId!, new RouteBinding(string.Empty, selector, RouteMatch.NoneOf, values));
                        break;
                    }
            }
        }

        // ③.6 容错策略（G4，机制侧）：Retry / Fallback / CatchRoute / TryGuard 合并到节点策略
        var failure = new Dictionary<string, NodeFailurePolicy>(StringComparer.Ordinal);
        NodeFailurePolicy Current(string nodeId) => failure.TryGetValue(nodeId, out var p) ? p : NodeFailurePolicy.None;
        foreach (var symbol in circuit.Symbols)
        {
            switch (symbol)
            {
                case RetrySymbol retry:
                    failure[retry.NodeId] = Current(retry.NodeId) with
                    { RetryMaxAttempts = retry.MaxAttempts, RetryBackoffBaseMs = retry.BackoffBaseMs };
                    break;
                case FallbackSymbol fallback:
                    failure[fallback.PrimaryNodeId] = Current(fallback.PrimaryNodeId) with
                    { FallbackNodeId = fallback.AlternativeNodeId };
                    break;
                case CatchRouteSymbol catchRoute:
                    failure[catchRoute.NodeId] = Current(catchRoute.NodeId) with
                    { CatchRouteNodeId = catchRoute.RemedyNodeId };
                    break;
                case TryGuardSymbol guard:
                    foreach (var protectedId in guard.ProtectedNodeIds)
                        failure[protectedId] = Current(protectedId) with { Guarded = true };
                    break;
            }
        }

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

        var frozenRoutes = new Dictionary<string, IReadOnlyList<RouteBinding>>(routes.Count, StringComparer.Ordinal);
        foreach (var pair in routes) frozenRoutes[pair.Key] = pair.Value;

        return new CircuitRuntimeIndex(nodesById, frozen, frozenRoutes, failure, entryPorts, exits);
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

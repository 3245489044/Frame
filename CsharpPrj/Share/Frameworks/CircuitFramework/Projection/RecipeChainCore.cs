namespace Share.CircuitFramework.Projection;

/// <summary>链式配方的一步（内核记录）：节点 Id + 步骤实现名 + 链端口 + 端口集（派生结果，供解析面复用）。</summary>
internal sealed record RecipeStepRecord(
    string NodeId,
    string StepKind,
    string ChainPort,
    IReadOnlyList<string> InPorts,
    IReadOnlyList<string> OutPorts);

/// <summary>
/// 链式配方内核（零领域、上下文无关）：唯一一份「顺序即语义」派生 —— 步骤表 → 唯一产品链
/// （相邻步自动成边；入口端口按选项供给每一步）+ 声明式附加边（真依赖，如跨步产物）+ 出口收集。
/// <para><see cref="RecipeBuilder"/>（无上下文声明面）与 <see cref="OrderedRecipe{TContext}"/>（带步骤体/解析面）
/// 共用本内核：端口编号、链边、出口缺省只在一处派生（声明面/执行面不漂移）。</para>
/// </summary>
internal sealed class RecipeChainCore
{
    /// <summary>缺省入口端口名（外部绑定产物，如 config）。</summary>
    internal const string DefaultEntryPort = "config";

    private readonly List<RecipeStepRecord> _steps = new();
    private readonly List<CircuitFlowSpec> _wires = new();
    private readonly List<string> _exits = new();
    private readonly List<CircuitLiteralSpec> _literals = new();
    private readonly List<CircuitRegionSpec> _regions = new();
    private readonly List<CircuitBranchSpec> _branches = new();
    private readonly List<CircuitSwitchSpec> _switches = new();
    private readonly List<string> _entryPorts;
    private readonly bool _entryOnEachStep;
    private bool _exitsDeclared;
    private CircuitRecipeSpec? _spec;

    /// <param name="circuitId">电路 Id（= 装配 Id）。</param>
    /// <param name="entryPorts">入口端口集（外部绑定产物）；缺省 = <see cref="DefaultEntryPort"/>。</param>
    /// <param name="entryOnEachStep">true = 每一步的 InPorts 自动含全部入口端口（顺序链缺省语义）；
    /// false = 入口仅声明在 spec.EntryPorts，步骤须显式声明所需入口端口（执行层按 InPorts 同名供给）。</param>
    internal RecipeChainCore(string circuitId, IReadOnlyList<string>? entryPorts, bool entryOnEachStep)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        CircuitId = circuitId;
        _entryPorts = entryPorts is null ? new List<string> { DefaultEntryPort } : new List<string>(entryPorts);
        _entryOnEachStep = entryOnEachStep;
    }

    internal string CircuitId { get; }

    /// <summary>步骤表（声明序即执行序；派生结果，供解析面按序解析）。</summary>
    internal IReadOnlyList<RecipeStepRecord> Steps => _steps;

    /// <summary>
    /// 常量注入（B3-1）：把 <paramref name="value"/> 直接绑到已声明步骤的入端口（不查上下文）。
    /// 节点/端口须已声明（拼错即抛，不静默产出无效声明）。
    /// </summary>
    internal void Literal(string nodeId, string port, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(port);

        var step = _steps.FirstOrDefault(s => s.NodeId == nodeId)
            ?? throw new InvalidOperationException($"常量注入指向未声明节点 '{nodeId}'（配方 '{CircuitId}'）。");
        if (step.InPorts.All(p => p != port))
            throw new InvalidOperationException(
                $"常量注入 '{nodeId}.{port}' 指向的入端口未在节点 '{nodeId}' 声明"
                + $"（可用：{string.Join(" | ", step.InPorts)}）。");

        _literals.Add(new CircuitLiteralSpec(nodeId, port, value));
        _spec = null;
    }

    /// <summary>
    /// 区域声明（G0-0）：把子计划（环 / 复用块）挂到**已声明步骤**内部。
    /// 承载节点与映射两端的**父侧**端口须已声明（拼错即抛，不静默产出无效声明）；
    /// 子侧端口与迭代上限的判据由编译期 <see cref="Compiler.RegionValidator"/> 统一给出（CFG-019~023）。
    /// <para>
    /// Kind 单源：承载步骤的 <c>StepKind</c> 被改写为 <see cref="CircuitRegionSpec.RegionKind"/>
    /// （区域节点的 Kind 是区域声明的事实，禁在 <c>.Step</c> 与 <c>.Region</c> 两处各写一遍）。
    /// </para>
    /// </summary>
    internal void Region(CircuitRegionSpec region)
    {
        ArgumentNullException.ThrowIfNull(region);

        var index = _steps.FindIndex(s => s.NodeId == region.NodeId);
        if (index < 0)
            throw new InvalidOperationException($"区域声明指向未声明节点 '{region.NodeId}'（配方 '{CircuitId}'）。");
        var step = _steps[index];

        foreach (var binding in region.InBindings)
            if (step.InPorts.All(p => p != binding.ParentPort))
                throw new InvalidOperationException(
                    $"区域入端口映射 '{region.NodeId}.{binding.ParentPort}' 的父端口未在节点 '{region.NodeId}' 声明"
                    + $"（可用：{string.Join(" | ", step.InPorts)}）。");

        foreach (var binding in region.OutBindings)
            if (step.OutPorts.All(p => p != binding.ParentPort))
                throw new InvalidOperationException(
                    $"区域出端口映射 '{region.NodeId}.{binding.ParentPort}' 的父端口未在节点 '{region.NodeId}' 声明"
                    + $"（可用：{string.Join(" | ", step.OutPorts)}）。");

        if (region.Plan?.JudgePort is { Length: > 0 } judge && step.InPorts.All(p => p != judge))
            throw new InvalidOperationException(
                $"区域判定端口 '{region.NodeId}.{judge}' 未在节点 '{region.NodeId}' 入端口声明"
                + $"（可用：{string.Join(" | ", step.InPorts)}）。");

        // Kind 单源：节点 Kind 由区域声明决定（消除 .Step 与 .Region 双写漂移）
        _steps[index] = step with { StepKind = region.RegionKind };

        _regions.Add(region);
        _spec = null;
    }
    internal void Add(
        string nodeId,
        string stepKind,
        IReadOnlyList<string>? inPorts = null,
        string? chainPort = null,
        IReadOnlyList<string>? outPorts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKind);
        if (_steps.Any(s => s.NodeId == nodeId))
            throw new InvalidOperationException($"顺序配方 '{CircuitId}' 重复声明节点 Id '{nodeId}'。");

        var index = _steps.Count;
        var port = string.IsNullOrWhiteSpace(chainPort) ? $"p{index + 1}" : chainPort;

        var ins = new List<string>();
        if (_entryOnEachStep) ins.AddRange(_entryPorts);
        if (inPorts is not null) ins.AddRange(inPorts);
        if (index > 0) ins.Add(_steps[index - 1].ChainPort);

        var outs = new List<string>();
        if (outPorts is not null) outs.AddRange(outPorts);
        outs.Add(port);

        _steps.Add(new RecipeStepRecord(nodeId, stepKind, port, ins, outs));
        _spec = null;
    }

    /// <summary>显式附加传递边（产物→依赖）：顺序链只连相邻步，跨步真依赖由此成边。</summary>
    internal void Wire(string fromNode, string fromPort, string toNode, string toPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromPort);
        ArgumentException.ThrowIfNullOrWhiteSpace(toNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPort);

        _wires.Add(new CircuitFlowSpec(fromNode, fromPort, toNode, toPort));
        _spec = null;
    }

    /// <summary>收集出口产物（最终产物集）：出口名由「节点 Id + 端口名」组合；未声明则缺省 = 末步链端口。</summary>
    internal void Collect(string nodeId, string port)
    {
        var step = _steps.FirstOrDefault(s => s.NodeId == nodeId)
            ?? throw new InvalidOperationException($"出口指向未声明节点 '{nodeId}'（配方 '{CircuitId}'）。");
        if (step.OutPorts.All(p => p != port))
            throw new InvalidOperationException(
                $"出口 '{nodeId}.{port}' 引用的输出端口未在节点 '{nodeId}' 声明（可用：{string.Join(" | ", step.OutPorts)}）。");

        _exits.Add($"{nodeId}.{port}");
        _exitsDeclared = true;
        _spec = null;
    }

    /// <summary>
    /// 分支声明（R1）：判定键为真 → then 激活 / else 抑制。承载节点与目标须已声明步骤（拼错即抛，不静默产出无效声明）；
    /// 判定键可解析性与目标存在性由编译期 CFG-026 判红。
    /// </summary>
    internal void Branch(string nodeId, string judgeKey, string thenNodeId, string? elseNodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(judgeKey);
        RequireStep(nodeId, "分支承载");
        RequireStep(thenNodeId, "分支 then 目标");
        if (elseNodeId is not null) RequireStep(elseNodeId, "分支 else 目标");

        _branches.Add(new CircuitBranchSpec(nodeId, judgeKey, thenNodeId, elseNodeId));
        _spec = null;
    }

    /// <summary>多路分派声明（R1）：选择键取值命中 route 激活 / 默认兜底。两端须已声明步骤。</summary>
    internal void Switch(
        string nodeId, string selectorKey, IReadOnlyDictionary<string, string> routes, string? defaultNodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        ArgumentNullException.ThrowIfNull(routes);
        RequireStep(nodeId, "分派承载");
        foreach (var kv in routes) RequireStep(kv.Value, $"分派 route '{kv.Key}' 目标");
        if (defaultNodeId is not null) RequireStep(defaultNodeId, "分派默认目标");

        _switches.Add(new CircuitSwitchSpec(nodeId, selectorKey, routes, defaultNodeId));
        _spec = null;
    }

    private void RequireStep(string nodeId, string what)
    {
        if (_steps.All(s => s.NodeId != nodeId))
            throw new InvalidOperationException($"{what}节点 '{nodeId}' 未在配方 '{CircuitId}' 声明。");
    }

    /// <summary>显式指定最终产物集（覆盖缺省与已声明收集项）。</summary>
    internal void SetExits(IEnumerable<string> exitPorts)
    {
        ArgumentNullException.ThrowIfNull(exitPorts);

        _exits.Clear();
        _exits.AddRange(exitPorts);
        _exitsDeclared = true;
        _spec = null;
    }

    /// <summary>声明面：投影输入（同一实例幂等，重复调用返回同一规格）。</summary>
    internal CircuitRecipeSpec Build()
    {
        if (_spec is not null) return _spec;
        if (_steps.Count == 0)
            throw new InvalidOperationException($"顺序配方 '{CircuitId}' 未声明任何步骤。");

        var steps = new List<CircuitStepSpec>(_steps.Count);
        var flows = new List<CircuitFlowSpec>(Math.Max(0, _steps.Count - 1) + _wires.Count);
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            steps.Add(new CircuitStepSpec(step.NodeId, step.StepKind, step.InPorts, step.OutPorts));

            if (i > 0)
            {
                var prev = _steps[i - 1].ChainPort;
                flows.Add(new CircuitFlowSpec(_steps[i - 1].NodeId, prev, step.NodeId, prev));
            }
        }
        flows.AddRange(_wires);

        var exits = _exitsDeclared
            ? _exits.ToArray()
            : new[] { $"{_steps[^1].NodeId}.{_steps[^1].ChainPort}" };

        _spec = new CircuitRecipeSpec(CircuitId, _entryPorts.ToArray(), exits, steps, flows,
            _literals.Count == 0 ? null : _literals.ToArray(),
            _regions.Count == 0 ? null : _regions.ToArray(),
            Branches: _branches.Count == 0 ? null : _branches.ToArray(),
            Switches: _switches.Count == 0 ? null : _switches.ToArray());
        return _spec;
    }
}

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

    /// <summary>追加一步（顺序即语义）：<paramref name="chainPort"/> 缺省 = <c>p{序号}</c>（唯一产品链端口）。</summary>
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

        _spec = new CircuitRecipeSpec(CircuitId, _entryPorts.ToArray(), exits, steps, flows);
        return _spec;
    }
}

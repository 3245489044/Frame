using Share.CircuitFramework.Core;
using Share.CircuitFramework.Execution;

namespace Share.CircuitFramework.Projection;

/// <summary>顺序配方步骤体：读具名输入端口 → 产出「该步骤唯一输出端口」的载荷（副作用由上下文闭包承载）。</summary>
/// <typeparam name="TContext">执行上下文（由调用方注入；顺序配方本身零领域依赖）。</typeparam>
public delegate Task<object?> StepBody<TContext>(
    TContext context,
    IReadOnlyDictionary<string, object?> inputs,
    CancellationToken cancellationToken);

/// <summary>顺序配方的一步：节点 Id + 步骤实现名（映射 ICircuitNode 执行体）+ 步骤体 + 可选输出端口名。</summary>
public sealed record OrderedStep<TContext>(
    string NodeId,
    string StepKind,
    StepBody<TContext> Body,
    string? OutPort = null);

/// <summary>
/// 顺序链配方构造器：「顺序即语义」的装配段 —— 步骤依次串成唯一产品链 p1→p2→…，每一步输出端口自动编号、
/// 相邻步的 Wire 自动连成链、入口端口默认供给每一步。
/// <para>声明面（<see cref="Build"/>，ctx 无关）与执行面（<see cref="Resolver"/>，绑定 ctx）由**同一份步骤表**派生：
/// 消除「配方手写端口 + 解析器手写 Kind 分支」的双源漂移。</para>
/// <para>通用件：零领域类型（上下文泛型注入；步骤名 / 端口名 / 载荷均由调用方给出）。</para>
/// </summary>
public sealed class OrderedRecipe<TContext>
{
    /// <summary>缺省入口端口名（外部绑定产物，如 config）。</summary>
    public const string DefaultEntryPort = "config";

    private readonly List<OrderedStep<TContext>> _steps = new();
    private readonly List<string> _entryPorts;
    private IReadOnlyList<string>? _exitPorts;
    private CircuitRecipeSpec? _spec;

    /// <param name="circuitId">电路 Id（= 装配 Id）。</param>
    /// <param name="entryPorts">入口端口集（外部绑定产物）；缺省 = <see cref="DefaultEntryPort"/>，每一步都声明该入口。</param>
    public OrderedRecipe(string circuitId, IReadOnlyList<string>? entryPorts = null)
    {
        if (string.IsNullOrWhiteSpace(circuitId))
            throw new ArgumentException("电路 Id 不可为空。", nameof(circuitId));

        CircuitId = circuitId;
        _entryPorts = entryPorts is null ? new List<string> { DefaultEntryPort } : new List<string>(entryPorts);
    }

    /// <summary>电路 Id（= 装配 Id）。</summary>
    public string CircuitId { get; }

    /// <summary>步骤表（声明序即执行序）。</summary>
    public IReadOnlyList<OrderedStep<TContext>> Steps => _steps;

    /// <summary>第 <paramref name="index"/> 步（0 基）的缺省输出端口名（唯一产品链）。</summary>
    public static string PortOf(int index) => $"p{index + 1}";

    /// <summary>追加一步（顺序即语义）；<paramref name="outPort"/> 缺省 = <see cref="PortOf"/>。</summary>
    public OrderedRecipe<TContext> Then(
        string nodeId, string stepKind, StepBody<TContext> body, string? outPort = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("步骤节点 Id 不可为空。", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(stepKind))
            throw new ArgumentException("步骤实现名不可为空。", nameof(stepKind));
        ArgumentNullException.ThrowIfNull(body);

        _steps.Add(new OrderedStep<TContext>(nodeId, stepKind, body, outPort));
        _spec = null;
        return this;
    }

    /// <summary>显式指定最终产物集（缺省 = 末步输出端口，形如 "NodeId.Port"）。</summary>
    public OrderedRecipe<TContext> WithExitPorts(params string[] exitPorts)
    {
        ArgumentNullException.ThrowIfNull(exitPorts);
        _exitPorts = exitPorts;
        _spec = null;
        return this;
    }

    /// <summary>声明面：投影输入（同一实例幂等，重复调用返回同一规格）。</summary>
    public CircuitRecipeSpec Build()
    {
        if (_spec is not null) return _spec;
        if (_steps.Count == 0)
            throw new InvalidOperationException($"顺序配方 '{CircuitId}' 未声明任何步骤。");

        var steps = new List<CircuitStepSpec>(_steps.Count);
        var flows = new List<CircuitFlowSpec>(Math.Max(0, _steps.Count - 1));
        for (var i = 0; i < _steps.Count; i++)
        {
            var inPorts = new List<string>(_entryPorts);
            if (i > 0) inPorts.Add(PortName(i - 1));
            steps.Add(new CircuitStepSpec(_steps[i].NodeId, _steps[i].StepKind, inPorts, new[] { PortName(i) }));

            if (i > 0)
            {
                var prevPort = PortName(i - 1);
                flows.Add(new CircuitFlowSpec(_steps[i - 1].NodeId, prevPort, _steps[i].NodeId, prevPort));
            }
        }

        var exitPorts = _exitPorts ?? new[] { $"{_steps[^1].NodeId}.{PortName(_steps.Count - 1)}" };
        _spec = new CircuitRecipeSpec(CircuitId, _entryPorts.ToArray(), exitPorts, steps, flows);
        return _spec;
    }

    /// <summary>
    /// 执行面：按同一份步骤表解析节点（输出端口与声明面同源）；步骤体未提供 / Kind 重复 → 显式抛（fail-fast）。
    /// </summary>
    public CircuitNodeResolver Resolver(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolver = new CircuitNodeResolver();
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var body = step.Body;
            var port = PortName(i);
            resolver.Register(step.NodeId, step.StepKind, DelegateCircuitNode.Signal(
                step.StepKind, port, (inputs, ct) => body(context, inputs, ct)));
        }
        return resolver;
    }

    private string PortName(int index) => _steps[index].OutPort ?? PortOf(index);
}

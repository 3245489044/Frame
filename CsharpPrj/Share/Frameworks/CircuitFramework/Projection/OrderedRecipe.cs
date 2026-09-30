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
/// <typeparam name="TContext">执行上下文（跨步骤共享的可变载体 ⇒ 引用类型约束，与组装框架 <c>AssemblyPhaseRunner&lt;TCtx&gt;</c> 同口径）。</typeparam>
public sealed class OrderedRecipe<TContext> where TContext : class
{
    /// <summary>缺省入口端口名（外部绑定产物，如 config）。</summary>
    public const string DefaultEntryPort = RecipeChainCore.DefaultEntryPort;

    private readonly List<OrderedStep<TContext>> _steps = new();
    private readonly RecipeChainCore _chain;

    /// <param name="circuitId">电路 Id（= 装配 Id）。</param>
    /// <param name="entryPorts">入口端口集（外部绑定产物）；缺省 = <see cref="DefaultEntryPort"/>，每一步都声明该入口。</param>
    public OrderedRecipe(string circuitId, IReadOnlyList<string>? entryPorts = null)
    {
        if (string.IsNullOrWhiteSpace(circuitId))
            throw new ArgumentException("电路 Id 不可为空。", nameof(circuitId));

        CircuitId = circuitId;
        _chain = new RecipeChainCore(circuitId, entryPorts, entryOnEachStep: true);
    }

    /// <summary>电路 Id（= 装配 Id）。</summary>
    public string CircuitId { get; }

    /// <summary>步骤表（声明序即执行序）。</summary>
    public IReadOnlyList<OrderedStep<TContext>> Steps => _steps;

    /// <summary>第 <paramref name="index"/> 步（0 基）的缺省输出端口名（唯一产品链）。</summary>
    public static string PortOf(int index) => $"p{index + 1}";

    /// <summary>追加一步（顺序即语义）；<paramref name="outPort"/> 缺省 = <see cref="PortOf"/>（= 唯一产品链端口）。</summary>
    public OrderedRecipe<TContext> Then(
        string nodeId, string stepKind, StepBody<TContext> body, string? outPort = null, IReadOnlyList<string>? inPorts = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("步骤节点 Id 不可为空。", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(stepKind))
            throw new ArgumentException("步骤实现名不可为空。", nameof(stepKind));
        ArgumentNullException.ThrowIfNull(body);

        _steps.Add(new OrderedStep<TContext>(nodeId, stepKind, body, outPort));
        _chain.Add(nodeId, stepKind, inPorts, outPort);
        return this;
    }

    /// <summary>显式指定最终产物集（缺省 = 末步输出端口，形如 "NodeId.Port"）。</summary>
    public OrderedRecipe<TContext> WithExitPorts(params string[] exitPorts)
    {
        ArgumentNullException.ThrowIfNull(exitPorts);
        _chain.SetExits(exitPorts);
        return this;
    }

    /// <summary>收集出口产物（出口名由「节点 Id + 端口名」组合；与 <see cref="WithExitPorts"/> 二选一）。</summary>
    public OrderedRecipe<TContext> Collect(string nodeId, string port)
    {
        _chain.Collect(nodeId, port);
        return this;
    }

    /// <summary>显式附加传递边（跨步真依赖）：顺序链只连相邻步，跨步产物由此成边，装配期即可判红。</summary>
    public OrderedRecipe<TContext> Wire(string fromNode, string fromPort, string toNode, string toPort)
    {
        _chain.Wire(fromNode, fromPort, toNode, toPort);
        return this;
    }

    /// <summary>常量注入（B3-1）：把 <paramref name="value"/> 直接绑到已声明步骤的入端口（不读入口/上游）。</summary>
    public OrderedRecipe<TContext> Literal(string nodeId, string port, object? value)
    {
        _chain.Literal(nodeId, port, value);
        return this;
    }

    /// <summary>声明面：投影输入（同一实例幂等，重复调用返回同一规格）。</summary>
    public CircuitRecipeSpec Build()
    {
        if (_steps.Count == 0)
            throw new InvalidOperationException($"顺序配方 '{CircuitId}' 未声明任何步骤。");

        return _chain.Build();
    }

    /// <summary>
    /// 执行面（声明面与执行面同源）：同一份步骤表 → 配方 + 入口绑定 → 框架统一驱动；
    /// 绑定装配经框架 <see cref="RecipeAssembly{TEntry}"/> 单点（消费方不再各写驱动外壳）。
    /// </summary>
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        TContext context,
        IReadOnlyList<EntryBinding<TContext>> entryBindings,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ExitBinding>? exitBindings = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entryBindings);

        return new RecipeAssembly<TContext>(Build(), entryBindings)
            .RunAsync(context, Resolver, cancellationToken, _ => exitBindings);
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

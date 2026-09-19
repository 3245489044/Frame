namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 求值帧（J2）：指令循环的**运行期状态**承载点 —— 依赖声明 / 依赖槽 / 调用结果表 / 投影产物。
/// <para>
/// 分工：依赖面（声明 + 槽）按声明序服务 <see cref="PortOp.Dep"/>；结果表服务 <see cref="PortOp.Project"/>
/// 的引用解析；投影产物是组合族的直出形态（<see cref="PortOp.Project"/> 逐字段累加）。
/// </para>
/// <para>
/// 注入袋（工厂表）随装配进入本帧：声明模型与 IR 零运行期成员，运行期依赖一律走执行侧。
/// </para>
/// </summary>
internal sealed class EvalFrame
{
    private readonly Dictionary<int, ComposedCall> _calls = new();
    private int _nextDependency;

    internal EvalFrame(
        string portKey, IReadOnlyList<DepStandard> declared, PortInjection injection,
        Action<IReadOnlyList<string>> notes)
    {
        PortKey = portKey;
        Declared = declared;
        Deps = new object?[declared.Count];
        Factories = injection.Factories;
        Steps = new ScriptedSteps.Host(portKey, declared, notes);
    }

    /// <summary>端口键（诊断定位用）。</summary>
    internal string PortKey { get; }

    /// <summary>步骤执行宿主（脚本步骤单源 <see cref="ScriptedSteps"/> 的接线面）。</summary>
    internal ScriptedSteps.Host Steps { get; }

    /// <summary>上一条指令产物（<c>Collect</c> 指令缺 <c>from</c> 时的收集源）。</summary>
    internal object? Last { get; set; }

    /// <summary>依赖声明（<c>Dep</c> 指令操作数按声明序；下标引用只指向更早者）。</summary>
    internal IReadOnlyList<DepStandard> Declared { get; }

    /// <summary>依赖槽（第 i 个 <c>Dep</c> 指令写 <c>Deps[i]</c>；构建期可读更早槽）。</summary>
    internal object?[] Deps { get; }

    /// <summary>数据半工厂表（<c>dep.factory</c> 解析；缺表且声明了工厂即 fail-fast）。</summary>
    internal IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories { get; }

    /// <summary>调用结果表（引用名 → 结果；<see cref="PortOp.Project"/> 的取值来源）。</summary>
    internal Dictionary<string, object?> Results { get; } = new(StringComparer.Ordinal);

    /// <summary>投影产物（出现首个 <see cref="PortOp.Project"/> 指令才建立；null = 非投影形态）。</summary>
    internal Dictionary<string, object?>? Product { get; private set; }

    /// <summary>取投影产物（不存在则建立）。</summary>
    internal Dictionary<string, object?> RequireProduct() => Product ??= new(StringComparer.Ordinal);

    /// <summary>取下一个依赖下标（<c>Dep</c> 指令执行序 = 声明序）。</summary>
    internal int TakeDependencyIndex() => _nextDependency++;

    /// <summary>调用解算（按指令序号缓存：同一指令每次求值只解一次）。</summary>
    internal ComposedCall ResolveCall(int instructionIndex, CallStandard call)
    {
        if (_calls.TryGetValue(instructionIndex, out var cached)) return cached;

        var resolved = PortComposedCall.Resolve(Declared, PortKey, call);
        _calls[instructionIndex] = resolved;
        return resolved;
    }
}

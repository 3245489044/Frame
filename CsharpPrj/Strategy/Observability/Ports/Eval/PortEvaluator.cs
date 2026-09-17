namespace Strategy.Observability.Ports;

using Strategy.Observability.Diagnostics;

/// <summary>
/// 端口求值引擎（R3-1）：五段固定流水线 <c>BindParams → BuildDeps → RunProgram → Project → Deliver</c>，
/// 消费唯一规格模型 <see cref="PortSpec"/>；**除 <c>switch (Op)</c> 外无通道分支**（路线 §2.7 I-4）。
/// <para>
/// 本批实现 <see cref="PortOp.Call"/>（reflective 族）；其余操作码为已登记待迁移项，遇到即 fail-fast
/// （不静默返回空）。调用路径的解析 / 实参绑定 / 异常归一全部经 <see cref="PortCall"/>（唯一实现点）。
/// </para>
/// <para>
/// 性能（RF-4）：指令分派用 <c>switch (Op)</c> 编译型分派（非字典查表）；方法解析结果由
/// <c>ReflectivePortResolver</c> 的缓存承担，不在热路径重复反射。
/// </para>
/// </summary>
public sealed class PortEvaluator
{
    /// <summary>进程内共享实例（无状态；<see cref="PortEvaluatorBinding"/> 缺省使用）。</summary>
    public static PortEvaluator Shared { get; } = new();

    /// <summary>求值（五段流水线；同步实现对外呈异步签名，与 <see cref="IPortBinding"/> 对齐）。</summary>
    public ValueTask<object?> EvaluateAsync(
        PortSpec spec, IPortInput input, EvalContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        ct.ThrowIfCancellationRequested();

        var arguments = BindParams(spec, input, context);              // ① 形参逐位绑定
        _ = BuildDeps();                                               // ② 依赖构建（reflective：空）
        var product = RunProgram(spec, arguments, context, ct);        // ③ 指令序列执行
        var projected = Project(product);                              // ④ 产物投影
        return ValueTask.FromResult(Deliver(projected));               // ⑤ 载体交付
    }

    /// <summary>
    /// ① 形参逐位绑定：仅单条 <see cref="PortOp.Call"/> 的规格存在形参（Args 位序即语义）；
    /// 转换与缺参口径逐字委托 <see cref="PortCall.BuildArguments"/>。
    /// </summary>
    private static object?[] BindParams(PortSpec spec, IPortInput input, EvalContext context)
    {
        if (spec.Program.Count != 1 || spec.Program[0].Op != PortOp.Call) return Array.Empty<object?>();

        var instruction = spec.Program[0];
        context.InstructionIndex = 0;
        context.OutSlot = instruction.Out;

        var binding = ToBinding(spec, instruction);
        return PortCall.BuildArguments(binding, PortCall.Resolve(binding), input);
    }

    /// <summary>② 依赖构建：reflective 族无依赖（R3-2 由 <c>Dep</c>/<c>New</c> 指令承接）。</summary>
    private static object?[] BuildDeps() => Array.Empty<object?>();

    /// <summary>③ 指令序列执行（SSA：结果写槽；本批仅 <see cref="PortOp.Call"/>）。</summary>
    private static object? RunProgram(
        PortSpec spec, object?[] arguments, EvalContext context, CancellationToken ct)
    {
        object? result = null;
        for (var index = 0; index < spec.Program.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var instruction = spec.Program[index];
            context.InstructionIndex = index;
            context.OutSlot = instruction.Out;

            result = instruction.Op switch
            {
                PortOp.Call => PortCall.Invoke(PortCall.Resolve(ToBinding(spec, instruction)), arguments),
                _ => throw new PortBindingViolationException(
                    spec.PortKey,
                    $"端口 '{spec.PortKey}' 的指令 '{instruction.Op}'（第 {index} 条）尚未迁移到求值引擎（R3-2/R3-3）"),
            };
        }

        return result;
    }

    /// <summary>④ 产物投影：reflective 族无显式投影 —— 调用返回值即产物（R3-2/R3-3 承接 <c>Project</c> 指令）。</summary>
    private static object? Project(object? product) => product;

    /// <summary>⑤ 载体交付：<c>value</c> = 产物原样（<c>issues</c> / <c>action</c> 载体随 R3-3 引入，届时按声明 fail-fast 而非静默）。</summary>
    private static object? Deliver(object? product) => product;

    /// <summary>指令 → 调用声明（目标 / 方法 / 实参声明；只承机制可见信息）。</summary>
    private static PortBindingSpec ToBinding(PortSpec spec, PortInstr instruction)
        => new(spec.PortKey, instruction.Target ?? string.Empty, instruction.Method ?? string.Empty, instruction.Args);
}

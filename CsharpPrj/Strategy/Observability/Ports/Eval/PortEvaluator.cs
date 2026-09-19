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
        => EvaluateAsync(spec, input, context, PortInjection.Empty, ct);

    /// <summary>
    /// 求值（带装配注入袋；J4a 起「工厂表 / 场景选择」等运行期成员经本参数进入执行侧，声明模型与 IR 零运行期成员）。
    /// <para>
    /// 执行口径（R3-2 起）：**按 <see cref="PortSpec.Program"/> 指令序列逐条执行**（声明序 = 执行序），
    /// 写槽结果（<see cref="PortInstr.Out"/> 非空）落入本次求值的作用域，供后续指令与投影读取；末条指令产物即直出产物。
    /// 未迁移操作码遇到即 fail-fast（不静默返回空）。
    /// </para>
    /// </summary>
    public ValueTask<object?> EvaluateAsync(
        PortSpec spec, IPortInput input, EvalContext context, PortInjection injection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(injection);

        ct.ThrowIfCancellationRequested();

        var scope = new EvalScope(spec.PortKey);    // ② 依赖构建（reflective：空；Dep 指令逐条累加）
        var dependencies = new List<object?>();
        object? product = null;

        for (var index = 0; index < spec.Program.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var instruction = spec.Program[index];                  // ③ 指令序列执行（SSA：结果写槽）
            context.InstructionIndex = index;
            context.OutSlot = instruction.Out;

            product = Instruct(spec, instruction, index, input, scope, dependencies, injection);

            if (!string.IsNullOrEmpty(instruction.Out)) scope.Set(instruction.Out!, product);
        }

        return ValueTask.FromResult(Deliver(spec, product));        // ④⑤ 产物投影 / 载体交付
    }

    /// <summary>
    /// 单条指令执行（**唯一分派点**：本文件是全内核唯一 <c>switch (Op)</c>；未迁移操作码入 <c>_</c> 臂 fail-fast）。
    /// </summary>
    private static object? Instruct(
        PortSpec spec, PortInstr instruction, int index, IPortInput input, EvalScope scope,
        List<object?> dependencies, PortInjection injection)
        => instruction.Op switch
        {
            PortOp.Call => PortCall.Invoke(
                PortCall.Resolve(ToBinding(spec, instruction)),
                PortCall.BuildArguments(ToBinding(spec, instruction), PortCall.Resolve(ToBinding(spec, instruction)), input)),

            _ => throw new PortBindingViolationException(
                spec.PortKey,
                $"端口 '{spec.PortKey}' 的指令 '{instruction.Op}'（第 {index} 条）尚未迁移到求值引擎（R3-2/R3-3）"),
        };

    /// <summary>④ 产物投影：本批无显式投影 —— 末条指令产物即产物（<c>Project</c> 指令承接显式投影）。</summary>
    private static object? Deliver(PortSpec spec, object? product) => product;

    /// <summary>指令 → 调用声明（目标 / 方法 / 实参声明；只承机制可见信息）。</summary>
    private static PortBindingSpec ToBinding(PortSpec spec, PortInstr instruction)
        => new(spec.PortKey, instruction.Target ?? string.Empty, instruction.Method ?? string.Empty, instruction.Args);
}

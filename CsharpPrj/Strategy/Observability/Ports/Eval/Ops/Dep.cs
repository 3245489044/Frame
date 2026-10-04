namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 依赖算子单源（J2-1 <see cref="PortOp.Dep"/>）：<c>Program</c> 内 <c>Dep</c> 指令按声明序执行，
/// 第 i 条写依赖槽 i（下标引用只能指向更早的依赖 ⇒ 构建拓扑无环）。
/// <para>构建委托 <see cref="ComposedDeps.BuildDep"/>：与旧绑定类共用同一实现（逐字等价，无第二份语义）。</para>
/// </summary>
internal static class PortDependency
{
    /// <summary>指令序列内的依赖声明（<c>Dep</c> 指令操作数，按声明序）。</summary>
    internal static IReadOnlyList<DepStandard> Declare(IReadOnlyList<PortInstr> program)
    {
        PortGuard.Required(program, nameof(program));

        var declared = new List<DepStandard>();
        foreach (var instruction in program)
            if (instruction.Op == PortOp.Dep && instruction.Operand is DepStandard dep)
                declared.Add(dep);

        return declared;
    }

    /// <summary>
    /// 构建第 <paramref name="index"/> 个依赖（写依赖槽；供后续指令与调用解算引用）。
    /// <para>写槽与 <c>ComposedDeps.Build</c> 同口径（第 i 条指令写槽 i；下标引用只指更早者）。</para>
    /// </summary>
    internal static object? Build(EvalFrame frame, int index, IPortInput input)
    {
        var dependency = ComposedDeps.BuildDep(
            frame.Declared, index, input, frame.Deps, frame.Factories, frame.PortKey);

        frame.Deps[index] = dependency;
        return dependency;
    }
}

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

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

/// <summary>
/// 依赖下标校验单源（AD6）：四份「范围检查 + 静默取数组」统一到这里。
/// <para>
/// <paramref name="visible"/> = 当前可见（已构建）依赖数：构建顺序内只能引用**更早**的依赖，
/// 前向引用 / 越界 / 负下标一律 fail-fast，不再返回 null 让下游把它当「缺省/不可绑定」。
/// </para>
/// </summary>
internal static class PortDependencyReference
{
    /// <summary>取已构建依赖（<paramref name="visible"/> 见类型注释）。</summary>
    internal static object? Require(
        object?[] deps, int reference, int visible, string portKey, string context)
    {
        if (reference >= 0 && reference < visible) return deps[reference];

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context} 引用了不可见的依赖下标 {reference}" +
            $"（可见 {visible} 个 / 声明 {deps.Length} 个；只能引用更早的依赖）");
    }

    /// <summary>取已声明的依赖规格（调用解算 / 脚本绑定用）。</summary>
    internal static DepStandard RequireDeclared(
        IReadOnlyList<DepStandard> declared, int reference, int visible, string portKey, string context)
    {
        if (reference >= 0 && reference < visible) return declared[reference];

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context} 引用了不可见的依赖下标 {reference}" +
            $"（可见 {visible} 个 / 声明 {declared.Count} 个；只能引用更早的依赖）");
    }
}

using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 装配执行器（配方的通用外壳）：配方校验（配方域产物图 + 电路环/端口/拓扑）→ 投影 → 编译 → 拓扑驱动执行
/// → 出口绑定回投。
/// <para>通用件：零领域类型 —— 入口值、节点解析、出口绑定均由调用方注入；失败即异常（不吞、不降级）。</para>
/// </summary>
public static class CircuitAssemblyRunner
{
    /// <summary>驱动一条装配配方（校验 → 投影 → 编译 → 执行 → 出口回投）。</summary>
    /// <param name="spec">声明式装配配方（拓扑单一真源）。</param>
    /// <param name="entry">入口端口 → 外部绑定值（键 = <see cref="CircuitRecipeSpec.EntryPorts"/> 名）。</param>
    /// <param name="resolveNode">节点 Kind → 黑盒实现（可用 <see cref="CircuitNodeResolver.Resolve"/> 传入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="exitBindings">出口绑定表（null/空 = 不回投；由节点体自写上下文）。</param>
    public static async Task<IReadOnlyDictionary<string, object?>> RunAsync(
        CircuitRecipeSpec spec,
        IReadOnlyDictionary<string, object?> entry,
        Func<string, ICircuitNode> resolveNode,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ExitBinding>? exitBindings = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(resolveNode);

        // ① 配方域产物图校验（产物唯一 / 缺失产物）判红
        var specErrors = RecipeGraphValidator.Validate(spec);
        if (specErrors.Count > 0)
            throw new InvalidOperationException(
                "[CircuitAssembly] 装配产物图校验失败：" + string.Join("；", specErrors));

        // ② 投影 + 编译（环 → CFG-002 / 拓扑不可排序 → CFG-020）
        var circuit = CircuitRecipeProjector.Project(spec);
        var plan = CircuitCompiler.Compile(circuit);

        // ③ 拓扑驱动执行，产物经 Wire 流转、Exit 收集
        var result = await CircuitEngine.ExecuteAsync(circuit, plan, resolveNode, entry, cancellationToken)
            .ConfigureAwait(false);

        // ④ 出口回投（声明面绑定）
        ApplyExitBindings(spec, result, exitBindings);
        return result;
    }

    /// <summary>
    /// 出口绑定回投：绑定指向未声明出口 → 显式抛（防「出口改名后静默投 null」）；
    /// 未绑定出口不报错（由节点体自写上下文的段无需绑定）。
    /// </summary>
    public static void ApplyExitBindings(
        CircuitRecipeSpec spec,
        IReadOnlyDictionary<string, object?> result,
        IReadOnlyList<ExitBinding>? exitBindings)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(result);
        if (exitBindings is null || exitBindings.Count == 0) return;

        var declared = spec.ExitPorts.ToHashSet(StringComparer.Ordinal);
        foreach (var binding in exitBindings)
        {
            if (!declared.Contains(binding.ExitPort))
                throw new InvalidOperationException(
                    $"出口绑定 '{binding.ExitPort}' 未在配方 '{spec.CircuitId}' 的 ExitPorts 中声明：绑定与配方真源不一致。");

            result.TryGetValue(binding.ExitPort, out var value);
            binding.Apply(value);
        }
    }
}

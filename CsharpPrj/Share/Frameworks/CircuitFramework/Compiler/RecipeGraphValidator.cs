using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 装配配方产物图校验（声明层判据，线程非安全、装配期调用一次）：
/// <list type="bullet">
///   <item>同一产物被多步骤产出 → Error（产物唯一）；</item>
///   <item>Wire 依赖了「无人产出且非入口绑定」的产物 → Error（缺失产物）；</item>
///   <item>步骤输入端口无人供给（无 Wire 进入、非入口绑定、无人产出）→ Error（缺失产物）。</item>
/// </list>
/// 与 <see cref="CircuitValidator"/> 分工：环 / 端口解析 / fan-in 由电路框架管；
/// 「产物唯一」与「缺失产物」是配方域 invariant（产物端口名在电路内部是节点局部概念，须在配方语义层收口）。
/// </summary>
public static class RecipeGraphValidator
{
    public static IReadOnlyList<string> Validate(CircuitRecipeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<string>();

        // ① 产物唯一：每只产物恰有一只产出者
        var producers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in spec.Steps)
            foreach (var p in s.OutPorts)
                if (!producers.TryAdd(p, s.Id))
                    errors.Add($"产物 '{p}' 被多步骤产出（'{producers[p]}' 与 '{s.Id}'）：装配产物必须唯一。");

        var entry = spec.EntryPorts.ToHashSet(StringComparer.Ordinal);

        // ② 缺失产物：Wire 源端口须有产出者，或由入口按同名端口绑定
        var stepIds = spec.Steps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var f in spec.Flows)
        {
            if (entry.Contains(f.FromPort)) continue;
            if (stepIds.Contains(f.FromNode) && producers.ContainsKey(f.FromPort)) continue;
            errors.Add($"依赖产物 '{f.FromPort}'（{f.FromNode}.{f.FromPort} → {f.ToNode}.{f.ToPort}）无任何产出者且非入口绑定：缺失产物。");
        }

        // ③ 输入未供给：每步 InPort 须被某条 Wire 进入，或由入口同名绑定，或有人产出（同名端口即产物）
        var wiredTargets = spec.Flows.Select(f => f.ToNode + "." + f.ToPort).ToHashSet(StringComparer.Ordinal);
        foreach (var s in spec.Steps)
            foreach (var p in s.InPorts)
                if (!wiredTargets.Contains(s.Id + "." + p) && !entry.Contains(p) && !producers.ContainsKey(p))
                    errors.Add($"步骤 '{s.Id}' 的输入端口 '{p}' 无任何 Wire 进入、非入口绑定、亦无人产出：缺失产物。");

        return errors;
    }

    /// <summary>是否无 Error 级问题。</summary>
    public static bool IsValid(CircuitRecipeSpec spec) => Validate(spec).Count == 0;
}

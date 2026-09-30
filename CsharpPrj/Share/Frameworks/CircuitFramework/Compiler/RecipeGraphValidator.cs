using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 装配配方产物图校验（声明层判据，线程非安全、装配期调用一次）：
/// <list type="bullet">
///   <item>同一产物被多步骤产出 → Error（产物唯一）；</item>
///   <item>Wire 依赖了「无人产出且非入口绑定」的产物 → Error（缺失产物）；</item>
///   <item>步骤输入端口无人供给（无 Wire 进入、非入口绑定、非常量注入、无人产出）→ Error（缺失产物）。</item>
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

        // ③ 输入未供给：判据单一来源 = IsSupplied（区域判定端口 CFG-020 复用同一探针，禁第二份实现）
        foreach (var s in spec.Steps)
            foreach (var p in s.InPorts)
                if (!IsSupplied(spec, s.Id, p))
                    errors.Add($"步骤 '{s.Id}' 的输入端口 '{p}' 无任何 Wire 进入、非入口绑定、非常量注入、亦无人产出：缺失产物。");

        return errors;
    }

    /// <summary>
    /// 输入端口是否已供给 ——「缺失产物 / 输入未供给」判据的**单一来源**（配方域 ③ 与区域域 CFG-020 共用）：
    /// Wire 进入 ∨ 入口同名绑定 ∨ 常量注入 ∨ 有人产出（同名端口即产物）。
    /// </summary>
    /// <param name="excludeSelfProducer">true = 不计**承载节点自产**（区域条件迭代的判定端口用严格口径：
    /// 判定值须来自上游/入口/常量；自产即等于把判定值接到自己的输出上、无外部事实来源）。</param>
    public static bool IsSupplied(CircuitRecipeSpec spec, string nodeId, string port, bool excludeSelfProducer = false)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Flows.Any(f =>
                string.Equals(f.ToNode, nodeId, StringComparison.Ordinal)
                && string.Equals(f.ToPort, port, StringComparison.Ordinal)))
            return true;

        if (spec.EntryPorts.Contains(port, StringComparer.Ordinal)) return true;

        if ((spec.Literals ?? Array.Empty<CircuitLiteralSpec>()).Any(l =>
                string.Equals(l.NodeId, nodeId, StringComparison.Ordinal)
                && string.Equals(l.Port, port, StringComparison.Ordinal)))
            return true;

        return spec.Steps.Any(s =>
            (!excludeSelfProducer || !string.Equals(s.Id, nodeId, StringComparison.Ordinal))
            && s.OutPorts.Contains(port, StringComparer.Ordinal));
    }

    /// <summary>是否无 Error 级问题。</summary>
    public static bool IsValid(CircuitRecipeSpec spec) => Validate(spec).Count == 0;
}

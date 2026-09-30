namespace Share.CircuitFramework.Projection;

/// <summary>
/// 入口绑定：入口端口名 → 外部取值（对称于 <see cref="ExitBinding"/>；出口把产物投回调用方，入口把外部产物投喂进电路）。
/// 使「入口端口名 ↔ 外部来源」只在一处声明，消除手写入口字典与配方 EntryPorts 的双源漂移。
/// </summary>
/// <typeparam name="TEntry">外部入口对象（调用方类型；配方层零领域依赖）。</typeparam>
public sealed record EntryBinding<TEntry>(string Port, Func<TEntry, object?> Read);

/// <summary>
/// 入口投影（与出口回投 ApplyExitBindings 同一判据口径）：绑定表 → 入口端口字典（键 = 配方 EntryPorts）。
/// </summary>
public static class CircuitEntry
{
    /// <summary>
    /// 构建入口端口字典（键 = <see cref="CircuitRecipeSpec.EntryPorts"/>）。
    /// 绑定与配方真源不一致 → 显式抛：端口缺绑定（漏喂）与绑定未声明（多余）都是装配期错误，禁静默缺席
    /// （执行层按 <c>TryGetValue</c> 供入口，漏键 ⇒ 端口静默缺席、下游按缺产物处理，属不可见故障）。
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Build<TEntry>(
        CircuitRecipeSpec spec,
        TEntry entry,
        IReadOnlyList<EntryBinding<TEntry>> bindings)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(bindings);

        var declared = spec.EntryPorts.ToHashSet(StringComparer.Ordinal);
        var bound = bindings.Select(b => b.Port).ToHashSet(StringComparer.Ordinal);

        var unbound = spec.EntryPorts.Where(p => !bound.Contains(p)).ToArray();
        if (unbound.Length > 0)
            throw new InvalidOperationException(
                $"配方 '{spec.CircuitId}' 的入口端口无绑定：{string.Join("；", unbound)}。");

        var undeclared = bindings.Where(b => !declared.Contains(b.Port)).Select(b => b.Port).ToArray();
        if (undeclared.Length > 0)
            throw new InvalidOperationException(
                $"入口绑定 '{string.Join("；", undeclared)}' 未在配方 '{spec.CircuitId}' 的 EntryPorts 中声明：绑定与配方真源不一致。");

        var entryValues = new Dictionary<string, object?>(bindings.Count, StringComparer.Ordinal);
        foreach (var binding in bindings)
            entryValues[binding.Port] = binding.Read(entry);

        return entryValues;
    }

    /// <summary>入口端口名清单（由绑定表派生 ⇒ 与 <see cref="Build{TEntry}"/> 同源，禁另行手写数组）。</summary>
    public static IReadOnlyList<string> PortNames<TEntry>(IReadOnlyList<EntryBinding<TEntry>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        return bindings.Select(b => b.Port).ToArray();
    }
}

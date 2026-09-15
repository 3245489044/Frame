namespace Strategy.Observability.Diagnostics;

/// <summary>
/// 端口预算视图（E19，纯计算结果）：端口面规模、用例面规模、死键（声明但无用例触达）与逐类型分布。
/// <para>
/// 动机：端口面只增不减时无人能回答「这个端口还有人用吗 / 某类型是否已成万能入口」——
/// 本件把这两问变成可机检指标（守卫据此设棘轮：死键 = 0、单类型键数须登记理由、端口/用例比不上升）。
/// </para>
/// </summary>
public sealed record PortBudgetReport(
    int PortCount,
    int CaseCount,
    IReadOnlyList<string> DeadPortKeys,
    IReadOnlyList<PortTypeBudget> PerType)
{
    /// <summary>有用例触达的端口键数。</summary>
    public int LivePortCount => PortCount - DeadPortKeys.Count;

    /// <summary>端口 / 用例比（用例为 0 时记 0：分母失义不报警）。</summary>
    public double PortsPerCase => CaseCount == 0 ? 0 : (double)PortCount / CaseCount;

    /// <summary>单类型键数上限内的类型（未超限者）。</summary>
    public IReadOnlyList<PortTypeBudget> TypesOver(int limit)
        => PerType.Where(t => t.PortKeys.Count >= limit).ToArray();
}

/// <summary>逐类型端口键分布（类型 = 端口键中首个 <c>.</c> 之前的段；无分隔符者整体为类型名）。</summary>
public sealed record PortTypeBudget(string TypeName, IReadOnlyList<string> PortKeys);

/// <summary>
/// 端口预算测量（E19）：<c>Measure(声明键, 用例触达键)</c> → <see cref="PortBudgetReport"/>。
/// 纯计算（无 IO、不抛领域异常）；键均为不透明字符串，按 Ordinal 比较。
/// </summary>
public static class PortBudget
{
    /// <summary>测量端口预算（两侧均为键集合；重复键自动去重，结果键 Ordinal 升序）。</summary>
    public static PortBudgetReport Measure(
        IEnumerable<string> declaredPortKeys, IEnumerable<string> usedPortKeys, int caseCount)
    {
        ArgumentNullException.ThrowIfNull(declaredPortKeys);
        ArgumentNullException.ThrowIfNull(usedPortKeys);
        if (caseCount < 0) throw new ArgumentOutOfRangeException(nameof(caseCount), caseCount, "用例数不得为负");

        var declared = new SortedSet<string>(declaredPortKeys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);
        var used = new HashSet<string>(usedPortKeys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);

        var dead = declared.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var perType = declared
            .GroupBy(TypeOf, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new PortTypeBudget(
                group.Key,
                group.OrderBy(k => k, StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new PortBudgetReport(declared.Count, caseCount, dead, perType);
    }

    /// <summary>端口键的类型段（首个 <c>.</c> 之前；无分隔符 → 整体）。</summary>
    public static string TypeOf(string portKey)
    {
        ArgumentNullException.ThrowIfNull(portKey);

        var separator = portKey.IndexOf('.', StringComparison.Ordinal);
        return separator <= 0 ? portKey : portKey[..separator];
    }
}

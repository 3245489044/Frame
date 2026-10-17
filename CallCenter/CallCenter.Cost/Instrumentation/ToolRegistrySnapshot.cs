namespace CallCenter.Cost.Instrumentation;

/// <summary>注册面漂移报告（只读，不阻断）。</summary>
public sealed record ToolRegistryDrift(
    IReadOnlyList<string> UndeclaredTools,
    IReadOnlyList<string> UnusedTools);

/// <summary>工具注册面快照（供 Observed/Declared 对账）。</summary>
public sealed record ToolRegistrySnapshot(IReadOnlyCollection<string> DeclaredToolNames)
{
    /// <summary>Observed ∖ Declared = 未注册却被调用；Declared ∖ Observed = 已注册零调用。</summary>
    public ToolRegistryDrift Compare(IEnumerable<string> observedToolNames)
    {
        var declared = new HashSet<string>(DeclaredToolNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var observed = new HashSet<string>(observedToolNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var undeclared = observed.Where(n => !declared.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var unused = declared.Where(n => !observed.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return new ToolRegistryDrift(undeclared, unused);
    }
}

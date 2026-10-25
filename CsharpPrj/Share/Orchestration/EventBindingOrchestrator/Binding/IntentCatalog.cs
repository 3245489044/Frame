namespace Share.EventBindingOrchestrator.Binding;

/// <summary>
/// 意图词表（B1）：<b>声明驱动</b>——受控动词清单来自配置声明（<see cref="TConfig.UIConfiguration.Intents"/>），
/// 通用框架零领域词汇。
/// <para>用途：装配期判定 <c>events.*</c> 的取值是否为已声明意图（门禁/诊断的判据源）；
/// 未声明名仍可按「函数/工作流名」解析，但不得被当作受控意图放行。</para>
/// </summary>
public sealed class IntentCatalog
{
    private readonly HashSet<string> _declared;

    private IntentCatalog(IEnumerable<string>? declared)
    {
        _declared = new HashSet<string>(
            (declared ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>空词表（未声明 ⇒ 无意图；零领域默认值）。</summary>
    public static IntentCatalog Empty { get; } = new(null);

    /// <summary>从整体 UI 声明构建（<c>intents</c> 段为唯一来源）。</summary>
    public static IntentCatalog From(TConfig.UIConfiguration? config)
        => config?.Intents is { Count: > 0 } names ? new IntentCatalog(names) : Empty;

    /// <summary>从显式声明集合构建（供测试/宿主注入）。</summary>
    public static IntentCatalog FromDeclarations(IEnumerable<string>? names)
        => names is null ? Empty : new IntentCatalog(names);

    /// <summary>是否为已声明意图。</summary>
    public bool IsIntent(string? name)
        => !string.IsNullOrWhiteSpace(name) && _declared.Contains(name);

    /// <summary>已声明意图快照（门禁/诊断用）。</summary>
    public IReadOnlyCollection<string> All => _declared;
}

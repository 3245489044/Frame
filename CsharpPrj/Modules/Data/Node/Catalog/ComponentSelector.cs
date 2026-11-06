namespace Modules.Node.Catalog;

/// <summary>组件描述（选择算法的最小元数据面；与具体组件类型解耦）。</summary>
/// <param name="Name">组件名（唯一，大小写不敏感）。</param>
/// <param name="AlwaysOn">是否恒入选。</param>
/// <param name="OnDemand">是否仅按需（不进关键词自动池）。</param>
/// <param name="Order">装配序（升序）。</param>
/// <param name="Trigger">触发词（关键词自动匹配，权重 2）。</param>
/// <param name="Tags">标签（关键词自动匹配，权重 1）。</param>
public sealed record NodeComponentDescriptor(
    string Name,
    bool AlwaysOn = false,
    bool OnDemand = false,
    int Order = 0,
    string? Trigger = null,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>是否配置触发词。</summary>
    public bool HasTrigger => !string.IsNullOrWhiteSpace(Trigger);

    /// <summary>是否关键词自动池候选（非 AlwaysOn 且非 OnDemand）。</summary>
    public bool IsAutoCandidate => !AlwaysOn && !OnDemand;
}

/// <summary>组件选择结果。</summary>
/// <param name="Selected">入选组件（装配序 = AlwaysOn → 点名 → 自动）。</param>
/// <param name="Unmet">点名但未登记的组件名（不阻断）。</param>
public sealed record NodeComponentSelection<TComponent>(
    IReadOnlyList<TComponent> Selected,
    IReadOnlyList<string> Unmet);

/// <summary>
/// 通用组件选择内核：AlwaysOn（Order 升序）→ 点名（请求序、去重、未登记入 Unmet）→
/// 关键词自动补足剩余名额（Name=3 / Trigger=2 / Tags=1，得分降序再名升序，截断到 maxAuto 总名额）。
/// <para>maxAuto 是**总名额**（含 AlwaysOn 与点名），不是自动追加数；&lt;=0 等价于「AlwaysOn + 点名」。</para>
/// </summary>
public static class NodeComponentSelector
{
    /// <summary>选择（纯函数，输入序不影响判定；输出序按装配规则）。</summary>
    /// <param name="components">全部候选组件。</param>
    /// <param name="descriptorOf">组件 → 描述。</param>
    /// <param name="requestedNames">点名组件（可空）。</param>
    /// <param name="goal">任务目标（关键词文本）。</param>
    /// <param name="userInput">用户输入（关键词文本）。</param>
    /// <param name="scopeId">作用域键（关键词文本）。</param>
    /// <param name="maxAuto">总名额（含 AlwaysOn 与点名；&lt;=0 = 关闭自动匹配）。</param>
    public static NodeComponentSelection<TComponent> Select<TComponent>(
        IEnumerable<TComponent> components,
        Func<TComponent, NodeComponentDescriptor> descriptorOf,
        IReadOnlyCollection<string>? requestedNames,
        string? goal,
        string? userInput,
        string? scopeId,
        int maxAuto)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(descriptorOf);

        var rows = components
            .Select(component => (Component: component, Descriptor: descriptorOf(component)))
            .ToArray();
        var byName = new Dictionary<string, TComponent>(StringComparer.OrdinalIgnoreCase);
        var canonicalNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Descriptor.Name)
                && byName.TryAdd(row.Descriptor.Name, row.Component))
            {
                canonicalNames[row.Descriptor.Name] = row.Descriptor.Name;
            }
        }

        var selected = new List<TComponent>();
        var unmet = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // AlwaysOn 恒入，Order 升序（同序按名升序，保证确定性）。
        foreach (var row in rows
                     .Where(row => row.Descriptor.AlwaysOn)
                     .OrderBy(row => row.Descriptor.Order)
                     .ThenBy(row => row.Descriptor.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(row.Descriptor.Name)) selected.Add(row.Component);
        }

        // 点名：请求序、去重；未登记入 Unmet，不阻断。
        if (requestedNames is not null)
        {
            foreach (var name in requestedNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (byName.TryGetValue(name, out var component)
                    && canonicalNames.TryGetValue(name, out var canonical))
                {
                    if (seen.Add(canonical)) selected.Add(component);
                }
                else
                {
                    unmet.Add(name);
                }
            }
        }

        // 关键词自动补足剩余名额（总名额扣减 AlwaysOn + 点名）。
        var remaining = Math.Max(0, maxAuto - selected.Count);
        if (remaining > 0)
        {
            var text = Concat(goal, userInput, scopeId);
            foreach (var row in rows
                         .Where(row => row.Descriptor.IsAutoCandidate && !seen.Contains(row.Descriptor.Name))
                         .Select(row => (row, score: Score(row.Descriptor, text)))
                         .Where(pair => pair.score > 0)
                         .OrderByDescending(pair => pair.score)
                         .ThenBy(pair => pair.row.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
                         .Take(remaining))
            {
                if (seen.Add(row.row.Descriptor.Name)) selected.Add(row.row.Component);
            }
        }

        return new NodeComponentSelection<TComponent>(selected, unmet);
    }

    private static string Concat(params string?[] parts)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part)) builder.Append(part).Append(' ');
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static int Score(NodeComponentDescriptor descriptor, string text)
    {
        if (text.Length == 0) return 0;
        if (Contains(text, descriptor.Name)) return 3;
        if (descriptor.Trigger is { Length: > 0 } && Contains(text, descriptor.Trigger)) return 2;
        if (descriptor.Tags is not null)
        {
            foreach (var tag in descriptor.Tags)
            {
                if (Contains(text, tag)) return 1;
            }
        }

        return 0;
    }

    private static bool Contains(string text, string keyword)
        => keyword.Length > 0
           && text.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal);
}

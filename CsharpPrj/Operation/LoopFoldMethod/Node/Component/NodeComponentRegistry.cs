using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 线程安全组件注册表（默认实现）—— name → <see cref="NodeComponent"/> 单一映射。
/// Register 为唯一落点；同名重复登记 → <see cref="InvalidOperationException"/>（防"第二套描述"漂移，TH-12）。
/// 纯元数据，无执行闭包；大小写不敏感比较，组件名唯一。
/// </summary>
public sealed class NodeComponentRegistry : INodeComponentRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NodeComponent> _byName;

    /// <summary>
    /// 空注册表（每次访问返回**新实例**，可作占位/测试初始态）。
    /// E-14：不得共享同一个可变实例，否则任一使用方的 Register 会污染其他使用方。
    /// </summary>
    public static NodeComponentRegistry Empty => new();

    /// <summary>新建空注册表。</summary>
    public NodeComponentRegistry()
        => _byName = new Dictionary<string, NodeComponent>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register(NodeComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (string.IsNullOrWhiteSpace(component.Name))
            throw new ArgumentException("组件名不可为空。", nameof(component));

        lock (_gate)
        {
            if (_byName.ContainsKey(component.Name))
                throw new InvalidOperationException(
                    $"组件已登记（单一声明原则，禁止重复装配）: {component.Name}");
            _byName[component.Name] = component;
        }
    }

    /// <inheritdoc />
    public NodeComponent Get(string name)
        => TryGet(name, out var c)
            ? c
            : throw new KeyNotFoundException($"组件未登记: {name}");

    /// <inheritdoc />
    public bool TryGet(string name, out NodeComponent component)
    {
        lock (_gate)
            return _byName.TryGetValue(name ?? string.Empty, out component!);
    }

    /// <summary>
    /// 上下文关键词自动选择 + 点名 + AlwaysOn（通用装配选择，把 Agent 技能关键词选择泛化到组件层）：
    /// AlwaysOn 依 Order 升序恒入 → 点名（按序、去重、未登记入 Unmet）→ 关键词自动补足剩余名额
    /// （autoPool = 非 AlwaysOn 且非 OnDemand 的组件，按 Name=3 / Trigger=2 / Tags=1 在 goal+userInput+scopeId 命中打分，
    /// 得分降序再名升序，截断到 <paramref name="maxAuto"/>）。未点名组件亦可由上下文自动入选。
    /// </summary>
    /// <param name="requestedNames">点名组件（可空；未登记入 Unmet）。</param>
    /// <param name="goal">任务目标（关键词匹配文本）。</param>
    /// <param name="userInput">用户输入（关键词匹配文本）。</param>
    /// <param name="scopeId">作用域键（如 stepKey/taskKey，关键词匹配文本）。</param>
    /// <param name="maxAuto">本次装配总名额（含 AlwaysOn 与点名；&lt;=0 = 关闭自动匹配，等价于点名 + AlwaysOn）。</param>
    public ComponentSelection ResolveWithContext(
        IReadOnlyCollection<string>? requestedNames,
        string? goal, string? userInput, string? scopeId, int maxAuto)
    {
        lock (_gate)
        {
            var selected = new List<NodeComponent>();
            var unmet = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // AlwaysOn 恒入，依 Order 升序。
            foreach (var c in _byName.Values.Where(c => c.AlwaysOn).OrderBy(c => c.Order))
                if (seen.Add(c.Name)) selected.Add(c);

            // 点名（按序、去重；未登记入 Unmet，不阻断）。
            if (requestedNames is not null)
            {
                foreach (var name in requestedNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (_byName.TryGetValue(name, out var comp))
                    {
                        if (seen.Add(comp.Name)) selected.Add(comp);
                    }
                    else unmet.Add(name);
                }
            }

            // 关键词自动补足「剩余名额」（autoPool = 非 AlwaysOn 且非 OnDemand）。
            // E-15：maxAuto 是总名额，须扣除 AlwaysOn + 点名已占用的名额，否则实际入选会变成 maxAuto（超编）。
            var remaining = Math.Max(0, maxAuto - selected.Count);
            if (remaining > 0)
            {
                var text = Concat(goal, userInput, scopeId).ToLowerInvariant();
                foreach (var c in _byName.Values
                             .Where(c => !c.AlwaysOn && !c.OnDemand && !seen.Contains(c.Name))
                             .Select(c => new { C = c, Score = Score(c, text) })
                             .Where(x => x.Score > 0)
                             .OrderByDescending(x => x.Score)
                             .ThenBy(x => x.C.Name, StringComparer.OrdinalIgnoreCase)
                             .Take(remaining))
                {
                    if (seen.Add(c.C.Name)) selected.Add(c.C);
                }
            }

            return new ComponentSelection(selected, unmet);
        }
    }

    private static string Concat(params string?[] parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
            if (!string.IsNullOrWhiteSpace(p)) sb.Append(p).Append(' ');
        return sb.ToString();
    }

    private static int Score(NodeComponent c, string text)
    {
        if (text.Length == 0) return 0;
        if (Contains(text, c.Name)) return 3;
        if (c.Trigger is { Length: > 0 } && Contains(text, c.Trigger)) return 2;
        if (c.Tags is not null)
            foreach (var tag in c.Tags)
                if (Contains(text, tag)) return 1;
        return 0;
    }

    private static bool Contains(string text, string keyword)
        => keyword.Length > 0 && text.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// D-01：与 <see cref="ResolveWithContext"/> 共用唯一选择核心（maxAuto=0 即「点名 + AlwaysOn」），
    /// 不再维护第二份等价投影（两份实现曾在去重/Unmet 口径上分叉）。
    /// </remarks>
    public ComponentSelection ResolveSelected(IReadOnlyCollection<string>? requestedNames)
        => ResolveWithContext(requestedNames, goal: null, userInput: null, scopeId: null, maxAuto: 0);
}

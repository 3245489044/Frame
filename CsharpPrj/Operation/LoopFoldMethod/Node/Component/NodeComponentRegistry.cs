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

    /// <summary>共享空实例（可作占位/测试初始态）。</summary>
    public static readonly NodeComponentRegistry Empty = new();

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

    /// <inheritdoc />
    public ComponentSelection ResolveSelected(IReadOnlyCollection<string>? requestedNames)
    {
        List<NodeComponent> selected;
        List<string> unmet;
        lock (_gate)
        {
            selected = _byName.Values
                .Where(c => c.AlwaysOn)
                .OrderBy(c => c.Order)
                .ToList();
            unmet = new List<string>();

            if (requestedNames is not null)
            {
                var added = new HashSet<string>(selected.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var name in requestedNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (_byName.TryGetValue(name, out var comp))
                    {
                        if (added.Add(comp.Name)) selected.Add(comp);
                    }
                    else unmet.Add(name);
                }
            }
        }
        return new ComponentSelection(selected, unmet);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Catalog;

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
            // 机制单源 = Modules.Node.Catalog.NodeComponentSelector（L1 纯函数）；
            // 本处只做 NodeComponent / ComponentSelection 的契约映射，不再维护第二份选择算法。
            var selection = NodeComponentSelector.Select(
                _byName.Values,
                static component => new NodeComponentDescriptor(
                    component.Name,
                    AlwaysOn: component.AlwaysOn,
                    OnDemand: component.OnDemand,
                    Order: component.Order,
                    Trigger: component.Trigger,
                    Tags: component.Tags),
                requestedNames,
                goal,
                userInput,
                scopeId,
                maxAuto);

            return new ComponentSelection(selection.Selected, selection.Unmet);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// D-01：与 <see cref="ResolveWithContext"/> 共用唯一选择核心（maxAuto=0 即「点名 + AlwaysOn」），
    /// 不再维护第二份等价投影（两份实现曾在去重/Unmet 口径上分叉）。
    /// </remarks>
    public ComponentSelection ResolveSelected(IReadOnlyCollection<string>? requestedNames)
        => ResolveWithContext(requestedNames, goal: null, userInput: null, scopeId: null, maxAuto: 0);
}

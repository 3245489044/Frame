using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 节点 Kind → 黑盒实现 的解析表（单一解析入口）：注册序即诊断清单，未注册 Kind 的报错文案单源 ——
/// 消除各消费方手写 <c>kind =&gt; kind switch { … _ =&gt; throw … }</c> 的重复与文案漂移。
/// <para>通用件：零领域类型（Kind 与实现均由调用方注入）。</para>
/// </summary>
public sealed class CircuitNodeResolver
{
    private readonly Dictionary<string, ICircuitNode> _byKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _kindToNodeId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal);
    private readonly List<string> _kinds = new();

    /// <summary>注册一个 Kind（节点 Id 取 Kind 自身）。</summary>
    public CircuitNodeResolver Register(string kind, ICircuitNode node) => Add(null, kind, node);

    /// <summary>注册并登记承载节点 Id（诊断可指名两道步骤）。</summary>
    public CircuitNodeResolver Register(string nodeId, string kind, ICircuitNode node) => Add(nodeId, kind, node);

    /// <summary>解析 Kind；未注册 → 显式抛（禁静默返回空实现）。签名可直接作 <c>Func&lt;string, ICircuitNode&gt;</c> 传执行层。</summary>
    public ICircuitNode Resolve(string kind)
    {
        if (_byKind.TryGetValue(kind, out var node)) return node;
        throw new InvalidOperationException($"未注册的电路节点 Kind：'{kind}'（已注册：{Allowed}）。");
    }

    /// <summary>解析 Kind（不抛版本）。</summary>
    public bool TryResolve(string kind, out ICircuitNode node) => _byKind.TryGetValue(kind, out node!);

    /// <summary>已注册 Kind 清单（注册序；装配对账 / 测试用）。</summary>
    public IReadOnlyList<string> Kinds => _kinds;

    /// <summary>允许串（诊断文案单一来源，由注册序派生）。</summary>
    public string Allowed => _kinds.Count == 0 ? "（空）" : string.Join(" | ", _kinds);

    private CircuitNodeResolver Add(string? nodeId, string kind, ICircuitNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("节点 Kind 不可为空。", nameof(kind));

        var effectiveNodeId = string.IsNullOrWhiteSpace(nodeId) ? kind : nodeId;
        if (_kindToNodeId.TryGetValue(kind, out var owner))
            throw new InvalidOperationException(
                $"电路节点 Kind '{kind}' 重复注册（'{owner}' 与 '{effectiveNodeId}'）：执行层按 Kind 解析，同段内 Kind 须唯一。");
        if (!_nodeIds.Add(effectiveNodeId))
            throw new InvalidOperationException($"电路节点 Id '{effectiveNodeId}' 重复注册：节点 Id 须唯一。");

        _byKind[kind] = node;
        _kindToNodeId[kind] = effectiveNodeId;
        _kinds.Add(kind);
        return this;
    }
}

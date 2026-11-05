using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Relations;

namespace Modules.Node.Spec;

/// <summary>继承解析结果（值 + 来源边：无显式声明 ⇒ 值 null、边 null）。</summary>
/// <param name="Value">解析到的值（null = 未声明）。</param>
/// <param name="SourceEdge">提供该值的继承边（起点自身声明 ⇒ null）。</param>
public sealed record InheritedAttribute(string? Value, NodeEdge? SourceEdge);

/// <summary>
/// 执行声明**沿边继承**（纯解析，不执行）：从起点沿声明边向上取第一个显式值，并返回来源边。
/// <para>
/// 缺省继承边集 = <see cref="ReferenceTypes.Owns"/>（最窄、可解释）；环 ⇒ 诊断
/// <c>execution.inherit-cycle</c> 并停止（取安全侧，不抛）。
/// </para>
/// </summary>
public sealed class AttributeInheritance
{
    private readonly INodeDiagnosticsSink _diagnostics;

    /// <param name="diagnostics">诊断出口。</param>
    public AttributeInheritance(INodeDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    /// <summary>解析属性（起点优先；未声明 ⇒ 沿边向上）。</summary>
    /// <param name="start">起点地址。</param>
    /// <param name="selector">从声明取目标属性（null/空 = 未声明）。</param>
    /// <param name="edges">全部边。</param>
    /// <param name="bindings">执行声明集。</param>
    /// <param name="inheritKinds">允许继承的边类型（null/空 ⇒ 仅 <c>Owns</c>）。</param>
    public InheritedAttribute Resolve(
        NodeAddress start,
        Func<NodeExecutionBinding, string?> selector,
        IReadOnlyList<NodeEdge> edges,
        IReadOnlyList<NodeExecutionBinding> bindings,
        IReadOnlyCollection<string>? inheritKinds = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(bindings);

        var allowed = new HashSet<string>(
            inheritKinds is { Count: > 0 } ? inheritKinds : new[] { ReferenceTypes.Owns },
            StringComparer.Ordinal);

        var byEndpoint = new Dictionary<string, NodeExecutionBinding>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding?.Address is null)
            {
                continue;
            }

            if (!byEndpoint.TryAdd(binding.Address.EndpointId, binding))
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "execution.binding-duplicate",
                    NodeDiagnosticSeverity.Warning,
                    "同一地址存在多条执行声明：保留首个（不静默覆盖）",
                    binding.Address.EndpointId));
            }
        }

        var current = start;
        NodeEdge? via = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            if (!visited.Add(current.EndpointId))
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "execution.inherit-cycle",
                    NodeDiagnosticSeverity.Error,
                    "执行声明继承出现环：停止解析（取安全侧）",
                    current.EndpointId));
                return new InheritedAttribute(null, via);
            }

            if (byEndpoint.TryGetValue(current.EndpointId, out var binding))
            {
                var value = selector(binding);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return new InheritedAttribute(value, via);
                }
            }

            var parentEdge = FindParentEdge(edges, current, allowed);
            if (parentEdge is null)
            {
                return new InheritedAttribute(null, null);
            }

            if (!NodeAddress.TryParse(parentEdge.From.Id, out var parent))
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "execution.inherit-endpoint-invalid",
                    NodeDiagnosticSeverity.Warning,
                    "继承边的父端点不是可解析的节点地址：停止解析（不跨域猜测）",
                    parentEdge.From.Id));
                return new InheritedAttribute(null, null);
            }

            via = parentEdge;
            current = parent;
        }
    }

    private static NodeEdge? FindParentEdge(
        IReadOnlyList<NodeEdge> edges,
        NodeAddress current,
        IReadOnlySet<string> allowed)
    {
        NodeEdge? found = null;
        foreach (var edge in edges)
        {
            if (edge.To is null || !edge.From.IsNode || !allowed.Contains(edge.Kind))
            {
                continue;
            }

            if (!string.Equals(edge.To.Id, current.EndpointId, StringComparison.Ordinal))
            {
                continue;
            }

            // 稳定选择：边键序数序首个（不依赖输入顺序）
            if (found is null || string.Compare(edge.EdgeKey, found.EdgeKey, StringComparison.Ordinal) < 0)
            {
                found = edge;
            }
        }

        return found;
    }
}

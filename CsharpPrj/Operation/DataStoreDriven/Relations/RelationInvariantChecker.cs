using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Relations;

/// <summary>一条不变量违反（不变量 id + 结构事实理由）。</summary>
public sealed record RelationViolation(string InvariantId, string Reason);

/// <summary>
/// 关系不变量检查器（声明驱动；kind 判定全在 <see cref="CheckAsync"/>，禁内核内联分支）。
/// <para>遍历预算 = 4096（有界，对齐 <c>QuantificationConstraint</c>）；超预算按违反处理并可见化。</para>
/// </summary>
public sealed class RelationInvariantChecker
{
    /// <summary>环检测遍历预算（步数上限；超限即拒绝，不静默截断）。</summary>
    public const int TraversalBudget = 4096;

    private readonly ReferenceGraphCoordinator _graph;

    public RelationInvariantChecker(ReferenceGraphCoordinator graph)
        => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

    /// <summary>检查单条不变量（无违反 ⇒ 空表）。</summary>
    public async Task<IReadOnlyList<RelationViolation>> CheckAsync(
        RelationInvariant invariant,
        ResourceIdentifier source,
        ResourceIdentifier target,
        IEntityExistenceProbe probe,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invariant);

        return invariant.Kind switch
        {
            "target_single_parent" => await SingleParentAsync(invariant, source, target, ct),
            "source_single_target" => await SingleTargetAsync(invariant, source, target, ct),
            "acyclic" => await AcyclicAsync(invariant, source, target, ct),
            "target_exists" => await TargetExistsAsync(invariant, target, probe, ct),
            _ => Array.Empty<RelationViolation>(), // many_to_many：只声明/审计，不拒绝
        };
    }

    private async Task<IReadOnlyList<RelationViolation>> SingleParentAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var incoming = await _graph.GetReferencesAsync(target, includeIncoming: true, ct);
        var parents = incoming
            .Where(r => r.IsIncoming
                        && string.Equals(r.RefType, invariant.Edge, StringComparison.Ordinal)
                        && !RelationEndpoints.SameEntity(r.Source, source))
            .Select(r => RelationEndpoints.KeyOf(r.Source))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return parents.Count == 0
            ? Array.Empty<RelationViolation>()
            : new[]
            {
                new RelationViolation(invariant.Id,
                    $"目标 '{RelationEndpoints.KeyOf(target)}' 已由 {string.Join(",", parents)} 持有 '{invariant.Edge}' 入边（单父约束）"),
            };
    }

    private async Task<IReadOnlyList<RelationViolation>> SingleTargetAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var outgoing = await _graph.GetReferencesAsync(source, includeIncoming: false, ct);
        var targets = outgoing
            .Where(r => !r.IsIncoming
                        && string.Equals(r.RefType, invariant.Edge, StringComparison.Ordinal)
                        && !RelationEndpoints.SameEntity(r.Target, target))
            .Select(r => RelationEndpoints.KeyOf(r.Target))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return targets.Count == 0
            ? Array.Empty<RelationViolation>()
            : new[]
            {
                new RelationViolation(invariant.Id,
                    $"源 '{RelationEndpoints.KeyOf(source)}' 已有 '{invariant.Edge}' 出边 → {string.Join(",", targets)}（单目标约束）"),
            };
    }

    private async Task<IReadOnlyList<RelationViolation>> AcyclicAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        // 从 target 沿同边类型出边走；若可达 source ⇒ 新增边成环。
        var queue = new Queue<ResourceIdentifier>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { RelationEndpoints.KeyOf(target) };
        queue.Enqueue(target);
        var steps = 0;

        while (queue.Count > 0)
        {
            if (++steps > TraversalBudget)
                return new[]
                {
                    new RelationViolation(invariant.Id,
                        $"环检测超出遍历预算 {TraversalBudget}（目标 '{RelationEndpoints.KeyOf(target)}' 起的 '{invariant.Edge}' 子图过大）"),
                };

            var current = queue.Dequeue();
            if (RelationEndpoints.SameEntity(current, source))
                return new[]
                {
                    new RelationViolation(invariant.Id,
                        $"加入 '{RelationEndpoints.KeyOf(source)}' → '{RelationEndpoints.KeyOf(target)}' 将形成 '{invariant.Edge}' 环"),
                };

            var outgoing = await _graph.GetReferencesAsync(current, includeIncoming: false, ct);
            foreach (var next in outgoing)
            {
                if (next.IsIncoming) continue;
                if (!string.Equals(next.RefType, invariant.Edge, StringComparison.Ordinal)) continue;
                if (!seen.Add(RelationEndpoints.KeyOf(next.Target))) continue;
                queue.Enqueue(next.Target);
            }
        }

        return Array.Empty<RelationViolation>();
    }

    private static async Task<IReadOnlyList<RelationViolation>> TargetExistsAsync(
        RelationInvariant invariant, ResourceIdentifier target, IEntityExistenceProbe probe, CancellationToken ct)
    {
        var exists = await probe.ExistsAsync(target.ResourceType ?? string.Empty, target.Id ?? string.Empty, ct);
        return exists
            ? Array.Empty<RelationViolation>()
            : new[]
            {
                new RelationViolation(invariant.Id,
                    $"目标端点 '{RelationEndpoints.KeyOf(target)}' 不存在（target_exists）"),
            };
    }
}

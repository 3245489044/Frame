using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 关系不变量检查器（声明驱动；kind 判定全在 <see cref="CheckAsync"/>，禁内核内联分支）。
/// <para>遍历预算 = 4096（有界）；超预算按违反处理并可见化（不拒绝，见内核 E9 口径）。</para>
/// </summary>
public sealed class RelationInvariantChecker
{
    public const int TraversalBudget = 4096;

    private readonly IResourceReferenceStore _reader;

    public RelationInvariantChecker(IResourceReferenceStore reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

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
            "many_to_many" => await ManyToManyAsync(invariant, source, target, ct),
            _ => Array.Empty<RelationViolation>(),
        };
    }

    /// <summary>
    /// 共享计数（结构事实：目标在同一 <paramref name="edge"/> 上的不同入边源个数）。
    /// 去重口径 = 源键 Ordinal 去重；预算内的计数为准确值，超预算返回 <see cref="TraversalBudget"/>
    /// （调用方须显式呈现「至少 N」而非伪造精确值）。
    /// </summary>
    public async Task<int> CountParentsAsync(
        string edge, ResourceIdentifier target, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edge);

        var incoming = await _reader.GetReferencesAsync(target, includeIncoming: true, ct);
        var count = incoming
            .Where(r => r.IsIncoming && string.Equals(r.RefType, edge, StringComparison.Ordinal))
            .Select(r => RelationEndpoints.KeyOf(r.Source))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return count > TraversalBudget ? TraversalBudget : count;
    }

    /// <summary>
    /// many_to_many（真共享 M:N）：共享本身合法，故不产生拒绝项；
    /// 仅在共享计数超出遍历预算时产出「可见化」违反（<see cref="RelationViolationKind.BudgetExceeded"/>，
    /// 内核按可见化处理，不拒绝写入）。
    /// </summary>
    private async Task<IReadOnlyList<RelationViolation>> ManyToManyAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var parents = await CountParentsExcludingAsync(invariant.Edge, target, source, ct);
        return parents.Count <= TraversalBudget
            ? Array.Empty<RelationViolation>()
            : new[]
            {
                new RelationViolation(invariant.Id, RelationViolationKind.BudgetExceeded,
                    $"共享计数超出遍历预算 {TraversalBudget}：目标 '{RelationEndpoints.KeyOf(target)}' 的 "
                    + $"'{invariant.Edge}' 入边来自 {parents.Count} 个源（many_to_many 只可见化，不拒绝）"),
            };
    }

    private async Task<List<string>> CountParentsExcludingAsync(
        string edge, ResourceIdentifier target, ResourceIdentifier source, CancellationToken ct)
    {
        var incoming = await _reader.GetReferencesAsync(target, includeIncoming: true, ct);
        return incoming
            .Where(r => r.IsIncoming
                        && string.Equals(r.RefType, edge, StringComparison.Ordinal)
                        && !RelationEndpoints.SameEntity(r.Source, source))
            .Select(r => RelationEndpoints.KeyOf(r.Source))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<RelationViolation>> SingleParentAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var incoming = await _reader.GetReferencesAsync(target, includeIncoming: true, ct);
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
                new RelationViolation(invariant.Id, RelationViolationKind.SingleParent,
                    $"目标 '{RelationEndpoints.KeyOf(target)}' 已由 {string.Join(",", parents)} 持有 '{invariant.Edge}' 入边（单父约束）"),
            };
    }

    private async Task<IReadOnlyList<RelationViolation>> SingleTargetAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var outgoing = await _reader.GetReferencesAsync(source, includeIncoming: false, ct);
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
                new RelationViolation(invariant.Id, RelationViolationKind.SingleTarget,
                    $"源 '{RelationEndpoints.KeyOf(source)}' 已有 '{invariant.Edge}' 出边 → {string.Join(",", targets)}（单目标约束）"),
            };
    }

    private async Task<IReadOnlyList<RelationViolation>> AcyclicAsync(
        RelationInvariant invariant, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var queue = new Queue<ResourceIdentifier>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { RelationEndpoints.KeyOf(target) };
        queue.Enqueue(target);
        var steps = 0;

        while (queue.Count > 0)
        {
            if (++steps > TraversalBudget)
                return new[]
                {
                    new RelationViolation(invariant.Id, RelationViolationKind.BudgetExceeded,
                        $"环检测超出遍历预算 {TraversalBudget}（目标 '{RelationEndpoints.KeyOf(target)}' 起的 '{invariant.Edge}' 子图过大）"),
                };

            var current = queue.Dequeue();
            if (RelationEndpoints.SameEntity(current, source))
                return new[]
                {
                    new RelationViolation(invariant.Id, RelationViolationKind.Cycle,
                        $"加入 '{RelationEndpoints.KeyOf(source)}' → '{RelationEndpoints.KeyOf(target)}' 将形成 '{invariant.Edge}' 环"),
                };

            var outgoing = await _reader.GetReferencesAsync(current, includeIncoming: false, ct);
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
                new RelationViolation(invariant.Id, RelationViolationKind.MissingEndpoint,
                    $"目标端点 '{RelationEndpoints.KeyOf(target)}' 不存在（target_exists）"),
            };
    }
}

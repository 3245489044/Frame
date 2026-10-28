using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace UniversalWorkflowFramework.StorageFramework.Delete;

/// <summary>
/// 通用删除入口（引擎级、零领域）：域 recipe 注册表 + 统一流水线
/// Resolve → Guard → Execute → Cascade → Verify → Report。
/// </summary>
public interface IDeleteFabric
{
    /// <summary>整批删除（逐项结果；禁整批静默）。</summary>
    Task<DeleteReceipt> DeleteAsync(DeleteRequest request, CancellationToken ct = default);

    /// <summary>取域 recipe（未注册 / 未解析 ⇒ false，调用方负责可见化拒绝）。</summary>
    bool TryGetRecipe(string domain, out IDeleteRecipe recipe);
}

/// <summary>
/// 删除流水线唯一实现。
/// <para>纪律：本类不含任何具体域分支；域差异只经 <see cref="IDeleteRecipe"/> 数据表达。</para>
/// <para>状态语义：未命中探针 ⇒ <c>Skipped</c>（已证不存在）；探针命中但删除未命中 ⇒ <c>Rejected(probe-delete-mismatch)</c>；
/// 删除后任一声明形态仍存在 ⇒ <c>Rejected(verify:still-exists)</c>（禁「视为已删除」）。</para>
/// </summary>
public sealed class DeleteFabric : IDeleteFabric
{
    private readonly Dictionary<string, IDeleteRecipe> _recipes = new(StringComparer.Ordinal);
    private readonly Func<string, IDeleteRecipe?>? _fallback;

    /// <param name="fallback">未显式注册域的兜底解析（如节点域载体）；null ⇒ 未注册域一律拒绝。</param>
    public DeleteFabric(Func<string, IDeleteRecipe?>? fallback = null)
        => _fallback = fallback;

    /// <summary>登记域配方（同域重复登记 = 后写覆盖；装配期只应一次）。</summary>
    public DeleteFabric Register(IDeleteRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        _recipes[recipe.Domain] = recipe;
        return this;
    }

    /// <inheritdoc />
    public bool TryGetRecipe(string domain, out IDeleteRecipe recipe)
    {
        recipe = null!;
        if (string.IsNullOrWhiteSpace(domain)) return false;
        if (_recipes.TryGetValue(domain.Trim(), out var registered))
        {
            recipe = registered;
            return true;
        }

        var resolved = _fallback?.Invoke(domain.Trim());
        if (resolved is null) return false;
        recipe = resolved;
        return true;
    }

    /// <inheritdoc />
    public async Task<DeleteReceipt> DeleteAsync(DeleteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var keys = request.SafeKeys;
        if (keys.Count == 0) return DeleteReceipt.Empty;

        if (!TryGetRecipe(request.Domain, out var recipe))
        {
            return new DeleteReceipt(keys
                .Select(k => DeleteItemOutcome.Rejected(k, $"domain-not-registered:{request.Domain}"))
                .ToArray());
        }

        var items = new List<DeleteItemOutcome>(keys.Count);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(await DeleteOneAsync(recipe, key, request, ct).ConfigureAwait(false));
        }

        return new DeleteReceipt(items);
    }

    private static async Task<DeleteItemOutcome> DeleteOneAsync(
        IDeleteRecipe recipe, string requestedKey, DeleteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
            return DeleteItemOutcome.Rejected(requestedKey ?? string.Empty, "key-empty");

        var candidates = (recipe.ResolveKeys(requestedKey) ?? Array.Empty<string>())
            .Where(static k => !string.IsNullOrWhiteSpace(k))
            .Select(static k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
            return DeleteItemOutcome.Rejected(requestedKey, "key-not-resolvable");

        var guard = await recipe.GuardAsync(candidates[0], request, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(guard))
            return DeleteItemOutcome.Rejected(requestedKey, $"guard:{guard}");

        string carrierKey;
        string? probeEvidence = null;
        var probed = new List<string>(candidates.Length);

        if (recipe.ProbeRequired)
        {
            DeleteProbe? hit = null;
            foreach (var candidate in candidates)
            {
                var probe = await recipe.ProbeAsync(candidate, ct).ConfigureAwait(false);
                probed.Add($"{candidate}:{(probe.Exists ? "hit" : "absent")}");
                if (!probe.Exists) continue;
                hit = probe;
                break;
            }

            if (hit is null)
            {
                // 幂等：全部声明形态都不存在 ⇒ 已验证的跳过（带已探测键清单，禁空口报成功）
                return DeleteItemOutcome.Skipped(
                    requestedKey, string.Join(",", probed), "目标在全部声明载体形态下均不存在（已验证）");
            }

            carrierKey = hit.CarrierKey ?? candidates[0];
            probeEvidence = $"{hit.Evidence}:{carrierKey}";
        }
        else
        {
            carrierKey = candidates[0];
        }

        var primary = await recipe.DeleteAsync(carrierKey, request, ct).ConfigureAwait(false);
        if (primary is null)
            return DeleteItemOutcome.Rejected(requestedKey, "carrier-returned-null", carrierKey, probeEvidence);

        if (primary.Status == BatchItemStatus.NotFound)
        {
            return recipe.ProbeRequired
                ? DeleteItemOutcome.Rejected(
                    requestedKey, $"probe-delete-mismatch:{primary.Message}", carrierKey, probeEvidence)
                : DeleteItemOutcome.NotFound(
                    requestedKey, primary.Message ?? "载体不存在", carrierKey, probeEvidence);
        }

        if (primary.Status is not (BatchItemStatus.Ok or BatchItemStatus.Skipped))
            return DeleteItemOutcome.Of(requestedKey, carrierKey, primary.Status, primary.Message, probeEvidence);

        var executed = new List<string>();
        if (request.Mode != DeleteMode.Direct)
        {
            var ordered = OrderCascades(recipe.Cascades, out var cycleError);
            if (cycleError is not null)
                return DeleteItemOutcome.Rejected(requestedKey, cycleError, carrierKey, probeEvidence, executed);

            foreach (var cascade in ordered)
            {
                var outcome = await cascade.Execute(carrierKey, request, ct).ConfigureAwait(false);
                if (outcome is not null && outcome.Status is not (BatchItemStatus.Ok or BatchItemStatus.Skipped))
                {
                    return DeleteItemOutcome.Rejected(
                        requestedKey, $"cascade:{cascade.Name}:{outcome.Message}", carrierKey, probeEvidence, executed);
                }

                executed.Add(cascade.Name);
            }
        }

        // Verify：声明的每一种形态都必须不存在（防「报成功但行还在」）
        if (recipe.ProbeRequired)
        {
            foreach (var candidate in candidates)
            {
                var after = await recipe.ProbeAsync(candidate, ct).ConfigureAwait(false);
                if (!after.Exists) continue;
                return DeleteItemOutcome.Rejected(
                    requestedKey, $"verify:still-exists:{candidate}", carrierKey, probeEvidence, executed);
            }
        }

        return DeleteItemOutcome.Ok(requestedKey, carrierKey, probeEvidence, executed, primary.Message);
    }


    /// <summary>级联依赖拓扑排序（声明序作稳定序；环 ⇒ <c>cascade-cycle</c>）。</summary>
    private static IReadOnlyList<DeleteCascade> OrderCascades(
        IReadOnlyList<DeleteCascade> cascades, out string? error)
    {
        error = null;
        if (cascades is null || cascades.Count == 0) return Array.Empty<DeleteCascade>();

        var byName = new Dictionary<string, DeleteCascade>(StringComparer.Ordinal);
        foreach (var cascade in cascades) byName[cascade.Name] = cascade;

        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var cascade in cascades)
        {
            indegree[cascade.Name] = 0;
            dependents[cascade.Name] = new List<string>();
        }

        foreach (var cascade in cascades)
        {
            foreach (var requirement in cascade.Requires ?? Array.Empty<string>())
            {
                if (!byName.ContainsKey(requirement)) continue; // 外部前置 = 已满足
                indegree[cascade.Name]++;
                dependents[requirement].Add(cascade.Name);
            }
        }

        var ready = cascades.Where(c => indegree[c.Name] == 0).Select(c => c.Name).ToList();
        var ordered = new List<DeleteCascade>(cascades.Count);
        while (ready.Count > 0)
        {
            var name = ready[0];
            ready.RemoveAt(0);
            ordered.Add(byName[name]);
            foreach (var next in dependents[name])
            {
                if (--indegree[next] == 0) ready.Add(next);
            }
        }

        if (ordered.Count != cascades.Count)
        {
            error = "cascade-cycle";
            return Array.Empty<DeleteCascade>();
        }

        return ordered;
    }
}

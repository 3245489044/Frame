using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 挂载读门面（UMF · L3）：MountId → 绑定 operation → edge 枚举；DeriveId → 派生声明。
/// <para>不做任何域分支；未装配/未声明 ⇒ 显式 <c>unavailable:</c> 空因（不伪装成「无数据」）。</para>
/// </summary>
public sealed class MountQueryService : IMountQuery
{
    private readonly IRelationPorts _ports;
    private readonly MountBindingCatalog _bindings;
    private readonly MountBatchRegistry? _batch;

    public MountQueryService(IRelationPorts ports, MountBindingCatalog bindings, MountBatchRegistry? batch = null)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _batch = batch;
    }

    /// <summary>
    /// 批量枚举（UMF · R3）：命中批量注册表 ⇒ **一次**域委托；未命中 ⇒ 逐 scope 回退并给
    /// <c>batch-fallback</c> 空因（可见化，禁静默 N+1）。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, MountSet>> EnumerateManyAsync(
        IReadOnlyList<MountScope> scopes, string mountId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, MountSet>(StringComparer.Ordinal);
        if (scopes is null || scopes.Count == 0) return result;

        var binding = _bindings.Resolve(mountId);
        if (binding is null)
        {
            foreach (var scope in scopes.Where(s => s is not null))
                result[scope.ContainerKey] = MountSet.Unavailable(mountId, "undeclared-mount");
            return result;
        }

        var reader = _batch?.TryGet(mountId);
        if (reader is null)
        {
            foreach (var scope in scopes.Where(s => s is not null))
            {
                var single = await EnumerateAsync(scope, mountId, ct).ConfigureAwait(false);
                result[scope.ContainerKey] = single with
                {
                    EmptyReasons = single.EmptyReasons.Concat(new[] { "batch-fallback" }).ToArray(),
                };
            }

            return result;
        }

        // 按 Namespace 分组批量读：此前只取第一个非空 ns 就把全部 scope 交给它，跨命名空间的容器会被读空。
        var groups = scopes
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.ContainerKey))
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Namespace) ? null : s.Namespace, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var keys = group.Select(s => s.ContainerKey).Distinct(StringComparer.Ordinal).ToList();
            var map = await reader(keys, group.Key, ct).ConfigureAwait(false);

            foreach (var key in keys)
            {
                var found = map.TryGetValue(key, out var members) && members is not null
                    ? members
                    : Array.Empty<string>();

                if (result.TryGetValue(key, out var existing))
                {
                    // 同一 ContainerKey 跨命名空间出现：并集，不后写覆盖前写。
                    found = existing.Keys.Concat(found).Distinct(StringComparer.Ordinal).ToArray();
                }

                result[key] = new MountSet(found,
                    found.Count == 0 ? new[] { "no-member" } : Array.Empty<string>(),
                    mountId, Fingerprint(mountId, found, Array.Empty<string>()));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MountSet> EnumerateAsync(
        MountScope scope, string mountId, CancellationToken ct = default)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.ContainerKey))
            return MountSet.Unavailable(mountId ?? string.Empty, "missing-scope");

        var binding = _bindings.Resolve(mountId);
        if (binding is null) return MountSet.Unavailable(mountId, "undeclared-mount");

        var operation = _ports.Operations.Resolve(binding.RelationOperation);
        if (operation is null) return MountSet.Unavailable(mountId, "undeclared-relation");

        var result = await _ports.Queries
            .EnumerateAsync(operation.Edge,
                new RelationScope(scope.ContainerKey, scope.Namespace, scope.ContainerEntity), ct)
            .ConfigureAwait(false);

        return CreateSet(mountId, result);
    }

    /// <inheritdoc />
    public async Task<MountSet> DeriveAsync(
        MountScope scope, string derivationId, CancellationToken ct = default)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.ContainerKey))
            return MountSet.Unavailable(derivationId ?? string.Empty, "missing-scope");

        if (string.IsNullOrWhiteSpace(derivationId))
            return MountSet.Unavailable(string.Empty, "missing-derivation");

        if (_ports.Operations.ResolveDerivation(derivationId) is null)
            return MountSet.Unavailable(derivationId, "derive-undeclared");

        var result = await _ports.Queries
            .DeriveAsync(derivationId,
                new RelationScope(scope.ContainerKey, scope.Namespace, scope.ContainerEntity), ct)
            .ConfigureAwait(false);

        return CreateSet(derivationId, result);
    }

    private static MountSet CreateSet(string id, RelationQueryResult result)
    {
        if (result.Keys.Count == 0)
            return new MountSet(result.Keys, result.EmptyReasons, id, Fingerprint(id, result.Keys, result.EmptyReasons));

        return new MountSet(result.Keys, result.EmptyReasons, id,
            Fingerprint(id, result.Keys, result.EmptyReasons));
    }

    /// <summary>读模型指纹（边集/键集内容哈希；与写侧事件同源口径，可用于缓存版本与对账）。</summary>
    private static string Fingerprint(
        string id, IReadOnlyList<string> keys, IReadOnlyList<string> emptyReasons)
    {
        var canonical = new StringBuilder(id);
        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
            canonical.Append('\u001f').Append(key);
        canonical.Append('\u001e');
        foreach (var reason in emptyReasons)
            canonical.Append(reason).Append('\u001f');

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes)[..16];
    }
}

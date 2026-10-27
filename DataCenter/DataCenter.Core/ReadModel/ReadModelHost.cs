#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataCenter.Protocol.Projection;
using Share.StateManagement;

namespace DataCenter.Core.ReadModel;

/// <summary>
/// 读模型宿主（唯一读面）：<c>ReadAsync&lt;T&gt;</c> = 取版本 → 缓存命中/回源 → 前后版本一致才写缓存 → 五态结果。
/// <para>
/// 缓存复用 <see cref="VersionedSnapshotCache{TValue}"/>（每模型一个实例；owner 维度隔离；无全局锁）。
/// </para>
/// </summary>
public sealed class ReadModelHost
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyArgs =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    private readonly ReadModelRegistry _registry;
    private readonly ConcurrentDictionary<string, VersionedSnapshotCache<object>> _caches =
        new(StringComparer.Ordinal);

    public ReadModelHost(ReadModelRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>按模型 id 读（owner 必填：缓存按 owner 隔离，禁跨 owner 串源）。</summary>
    public async Task<ReadOutcome<T>> ReadAsync<T>(
        string modelId,
        IReadOnlyDictionary<string, string?>? args,
        object owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!_registry.TryGet(modelId, out var spec, out var projector, out var probe))
            return ReadOutcome<T>.NotAttached("readmodel.not-registered:" + (modelId ?? string.Empty));

        var version = ReadModelVersionProbe.Resolve(spec, probe, owner);
        var cache = _caches.GetOrAdd(spec.ModelId, _ => new VersionedSnapshotCache<object>(spec.TtlMs ?? 0));

        if (cache.TryGet(owner, version, out var cached))
            return cached is T typed
                ? ReadOutcome<T>.Ok(typed)
                : ReadOutcome<T>.Violation("readmodel.type-mismatch:" + spec.ModelId);

        ReadOutcome<object> projected;
        try
        {
            projected = await projector
                .ProjectAsync(new ReadModelContext(spec, args ?? EmptyArgs, owner, version), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReadOutcome<T>.Violation("readmodel.project-failed:" + spec.ModelId + ":" + ex.GetType().Name);
        }

        switch (projected.Kind)
        {
            case ReadOutcomeKind.Empty:
                return ReadOutcome<T>.Empty();
            case ReadOutcomeKind.NotAttached:
                return ReadOutcome<T>.NotAttached(projected.Diagnostic ?? "readmodel.not-attached:" + spec.ModelId);
            case ReadOutcomeKind.Stale:
                return projected.Value is T staleValue
                    ? ReadOutcome<T>.Stale(staleValue, projected.Diagnostic ?? "readmodel.stale:" + spec.ModelId)
                    : ReadOutcome<T>.Violation("readmodel.type-mismatch:" + spec.ModelId);
            case ReadOutcomeKind.Violation:
                return ReadOutcome<T>.Violation(projected.Diagnostic ?? "readmodel.violation:" + spec.ModelId);
        }

        if (projected.Value is null)
            return ReadOutcome<T>.Violation("readmodel.null-value:" + spec.ModelId);
        if (projected.Value is not T value)
            return ReadOutcome<T>.Violation("readmodel.type-mismatch:" + spec.ModelId);

        var after = ReadModelVersionProbe.Resolve(spec, probe, owner);
        if (after != version)
            return ReadOutcome<T>.Stale(value, "readmodel.version-changed:" + spec.ModelId);

        cache.Put(owner, version, value);
        return ReadOutcome<T>.Ok(value);
    }

    /// <summary>指定 owner 最近一次读是否命中缓存（遥测/诊断口径；无 owner 级汇总）。</summary>
    public bool WasCacheHit(string modelId, object owner)
        => !string.IsNullOrWhiteSpace(modelId)
           && owner is not null
           && _caches.TryGetValue(modelId.Trim(), out var cache)
           && cache.LastHit(owner);
}


#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using ECS.Core;

namespace FrontEndDriven.WinForms.Applier;

/// <summary>
/// AppliedSnapshot 目标快照注册表（R1·快照驱动投影，渲染框架架构级重构方案 §四 R1）。
/// per-entity 上次成功应用的目标属性（BuildPropertyDict(desc) 原样产物，ECS form 相对基准），
/// 差异比较在数据层完成（新快照 vs 上次快照），零物理依赖、零豁免堆叠。
/// 生命周期：订阅 EntityManager.EntityDestroying——实体销毁自动移除快照（scope 销毁 = 实体逐个销毁，
/// 覆盖批量失效场景），杜绝「残留快照 → 虚假差异」（方案 §六 R1 风险缓解）。
/// </summary>
public sealed class AppliedSnapshotRegistry : IDisposable
{
    private readonly EntityManager? _entityManager;
    private readonly ConcurrentDictionary<Entity, Dictionary<string, object?>> _snapshots = new();
    private bool _disposed;

    public AppliedSnapshotRegistry(EntityManager? entityManager = null)
    {
        _entityManager = entityManager;
        if (entityManager != null)
            entityManager.EntityDestroying += OnEntityDestroying;
    }

    /// <summary>读取实体快照（无快照 → 调用方按「新控件/首渲全量应用」语义处理）。</summary>
    public bool TryGet(Entity entity, out Dictionary<string, object?> snapshot)
        => _snapshots.TryGetValue(entity, out snapshot!);

    /// <summary>写入/覆盖实体快照（应用成功后回写；存入不可变副本，隔离调用方后续修改）。</summary>
    public void Set(Entity entity, Dictionary<string, object?> snapshot)
    {
        if (snapshot == null)
            return;

        _snapshots[entity] = new Dictionary<string, object?>(snapshot, System.StringComparer.Ordinal);
    }

    public bool Remove(Entity entity) => _snapshots.TryRemove(entity, out _);

    public void Clear() => _snapshots.Clear();

    public int Count => _snapshots.Count;

    private void OnEntityDestroying(Entity entity) => _snapshots.TryRemove(entity, out _);

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_entityManager != null)
            _entityManager.EntityDestroying -= OnEntityDestroying;

        _snapshots.Clear();
        _disposed = true;
        System.GC.SuppressFinalize(this);
    }
}

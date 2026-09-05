using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.History;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 统一执行标识符服务（编排层，SingleImplementation / TH-12）：只编排，写边统一委托
/// <see cref="IResourceReferenceStore"/>（图元数据为真源，StateRootSingleSource）；读传导沿引用边向上找生效标志。
/// 装配可空：未装配 store / relations 时读返回默认、写 no-op，不阻断既有 SchedulingMode / TaskPool 路径（向后兼容，零回归）。
/// 权威设计：执行标识符框架设计 §8 + §6。
/// </summary>
public sealed class ExecutionFlagService : IExecutionFlagService
{
    private readonly IResourceReferenceStore? _store;
    private readonly IHistoryRelationManager? _relations;
    private readonly FlagConductor _conductor;

    /// <param name="store">图资源存储端口（可空；未装配则各方法 no-op/返回默认）。</param>
    /// <param name="relations">历史关系管理端口（可空；用于 history 作用域批量枚举）。</param>
    /// <param name="conductor">后台反应式驱动（可空；缺省自建）。</param>
    public ExecutionFlagService(
        IResourceReferenceStore? store,
        IHistoryRelationManager? relations = null,
        FlagConductor? conductor = null)
    {
        _store = store;
        _relations = relations;
        _conductor = conductor ?? new FlagConductor(this);
    }

    /// <summary>后台反应式驱动（供组合根注入 runPort：把 Llm/Rule 落到既有任务池执行）。</summary>
    public FlagConductor Conductor => _conductor;

    /// <summary>是否已装配图资源存储（供调用方判空诊断）。</summary>
    public bool IsAttached => _store is not null;

    /// <inheritdoc />
    public ExecutionFlag? GetFlag(EntityRef entity)
    {
        if (_store is null) return null;
        var res = _store.GetResourceAsync(entity.Normalize().ToResourceIdentifier()).GetAwaiter().GetResult();
        return ReadFlag(res);
    }

    /// <inheritdoc />
    public async Task<ExecutionFlag> ResolveFlagAsync(EntityRef entity, CancellationToken ct = default)
    {
        if (_store is null) return ExecutionFlag.Deterministic;
        var self = await ReadFlagAsync(entity.Normalize(), ct).ConfigureAwait(false);
        if (self is not null && !self.Value.IsInherit()) return self.Value;
        var up = await ResolveUpAsync(entity.Normalize(), new HashSet<string>(System.StringComparer.Ordinal), ct).ConfigureAwait(false);
        return up ?? ExecutionFlag.Deterministic;
    }

    /// <inheritdoc />
    public async Task SetFlagAsync(EntityRef entity, ExecutionFlag flag, CancellationToken ct = default)
    {
        if (_store is null) return;
        var norm = entity.Normalize();
        await ApplyFlagAsync(norm, flag, ct).ConfigureAwait(false);
        _conductor.Signal(norm); // 反应式：写后自动触发 解析→路由→运行→回切 Std
    }

    /// <inheritdoc />
    public async Task SetFlagScopeAsync(FlagScope scope, ExecutionFlag flag, CancellationToken ct = default)
    {
        if (_store is null) return;
        switch (scope.Kind)
        {
            case FlagScopeKind.TaskNodes:
                if (scope.Targets is null || scope.Targets.Count == 0)
                {
                    // task 全部 nodes：沿 cross_db_link 下游
                    foreach (var node in await GetDownstreamNodesAsync(new EntityRef(EntityKind.Task, scope.OwnerId), ct).ConfigureAwait(false))
                        await ApplyFlagAsync(node, flag, ct).ConfigureAwait(false);
                }
                else
                {
                    var wf = string.IsNullOrWhiteSpace(scope.WorkflowId) ? EntityRef.DefaultWorkflowId : scope.WorkflowId;
                    foreach (var nk in scope.Targets)
                        await ApplyFlagAsync(new EntityRef(EntityKind.Node, nk, wf), flag, ct).ConfigureAwait(false);
                }
                break;

            case FlagScopeKind.HistoryTasks:
                var tasks = scope.Targets is null or { Count: 0 }
                    ? (_relations is null ? (IReadOnlyList<string>)System.Array.Empty<string>() : await _relations.GetTaskKeysAsync(scope.OwnerId, ct).ConfigureAwait(false))
                    : scope.Targets.ToList();
                foreach (var tk in tasks)
                {
                    await ApplyFlagAsync(new EntityRef(EntityKind.Task, tk), flag, ct).ConfigureAwait(false);
                    foreach (var node in await GetDownstreamNodesAsync(new EntityRef(EntityKind.Task, tk), ct).ConfigureAwait(false))
                        await ApplyFlagAsync(node, flag, ct).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <inheritdoc />
    public Task<FlagLifecycleState> StartAsync(EntityRef entity, CancellationToken ct = default)
        => UpdateLifecycleAsync(entity.Normalize(), FlagLifecycleState.Running, ct);

    /// <inheritdoc />
    public Task<FlagLifecycleState> CompleteAsync(EntityRef entity, CancellationToken ct = default)
        => UpdateLifecycleAsync(entity.Normalize(), FlagLifecycleState.Completed, ct);

    /// <inheritdoc />
    public async Task ResetAsync(EntityRef entity, CancellationToken ct = default)
    {
        if (_store is null) return;
        var norm = entity.Normalize();
        await UpdateLifecycleCoreAsync(norm, FlagLifecycleState.Idle, ct).ConfigureAwait(false);
        await WriteFlagAsync(norm, ExecutionFlag.Std, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityRef>> ListFlaggedAsync(CancellationToken ct = default)
    {
        if (_store is null) return System.Array.Empty<EntityRef>();
        var all = await _store.GetAllResourcesAsync(ct).ConfigureAwait(false);
        var list = new List<EntityRef>();
        foreach (var res in all)
        {
            if (res.Metadata is not null && res.Metadata.ContainsKey(ExecutionFlagMetadata.FlagKey)
                && TryFromResourceIdentifier(res.Identifier, out var e))
                list.Add(e);
        }
        return list;
    }

    /// <summary>更新生命周期状态（供 <see cref="FlagConductor"/> 反应式驱动使用）。未装配 store → 原样返回，不写。</summary>
    internal Task<FlagLifecycleState> UpdateLifecycleAsync(EntityRef entity, FlagLifecycleState state, CancellationToken ct)
        => _store is null
            ? Task.FromResult(state)
            : UpdateLifecycleCoreAsync(entity, state, ct).ContinueWith(t => state, ct);

    /// <summary>写运行结果摘要（供 <see cref="FlagConductor"/> 使用）。未装配 store → no-op。</summary>
    internal async Task WriteResultAsync(EntityRef entity, ExecutionFlag flag, string status, CancellationToken ct)
    {
        if (_store is null) return;
        await UpdateMetadataAsync(entity.ToResourceIdentifier(), ExecutionFlagMetadata.ResultKey, $"{flag}:{status}", ct).ConfigureAwait(false);
    }


    // ---- 私有实现 ----

    private async Task<ExecutionFlag?> ReadFlagAsync(EntityRef entity, CancellationToken ct)
    {
        if (_store is null) return null;
        var res = await _store.GetResourceAsync(entity.ToResourceIdentifier(), ct).ConfigureAwait(false);
        return ReadFlag(res);
    }

    private static ExecutionFlag? ReadFlag(Resource? res)
    {
        if (res?.Metadata is null) return null;
        if (res.Metadata.TryGetValue(ExecutionFlagMetadata.FlagKey, out var v) && v is string s
            && System.Enum.TryParse<ExecutionFlag>(s, ignoreCase: true, out var f))
            return f;
        return null;
    }

    /// <summary>写 exec.flag（读既有 → 合并 → 更新，保证不冲掉既有元数据）。</summary>
    private async Task WriteFlagAsync(EntityRef entity, ExecutionFlag flag, CancellationToken ct)
        => await UpdateMetadataAsync(entity.ToResourceIdentifier(), ExecutionFlagMetadata.FlagKey, flag.ToString(), ct).ConfigureAwait(false);

    /// <summary>应用标志（写 exec.flag + lifecycle=flagged），**不触发信号** —— 供批量作用域 <see cref="SetFlagScopeAsync"/> 使用，
    /// 使声明可持久（供任务池消费），避免反应式运行后回切 Std 覆盖批量声明。</summary>
    private async Task ApplyFlagAsync(EntityRef entity, ExecutionFlag flag, CancellationToken ct)
    {
        await WriteFlagAsync(entity, flag, ct).ConfigureAwait(false);
        await UpdateLifecycleCoreAsync(entity, FlagLifecycleState.Flagged, ct).ConfigureAwait(false);
    }

    private Task UpdateLifecycleCoreAsync(EntityRef entity, FlagLifecycleState state, CancellationToken ct)
        => UpdateMetadataAsync(entity.ToResourceIdentifier(), ExecutionFlagMetadata.LifecycleKey, ExecutionFlagLifecycle.ToString(state), ct);

    /// <summary>读既有资源元数据 → 合并写入单键 → 更新（upsert；图元数据为真源）。</summary>
    private async Task UpdateMetadataAsync(ResourceIdentifier rid, string key, object value, CancellationToken ct)
    {
        var existing = await _store!.GetResourceAsync(rid, ct).ConfigureAwait(false);
        var meta = existing?.Metadata is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(existing.Metadata);
        meta[key] = value;
        await _store.UpdateResourceAsync(rid, meta, symbol: null, ct).ConfigureAwait(false);
    }

    /// <summary>沿引用边向上找最近非 Auto/Std 父层标志（visited 防环）。</summary>
    private async Task<ExecutionFlag?> ResolveUpAsync(EntityRef entity, HashSet<string> visited, CancellationToken ct)
    {
        if (_store is null) return null;
        var rid = entity.ToResourceIdentifier();
        if (!visited.Add(rid.GetUniqueKey())) return null;
        var refs = await _store.GetReferencesAsync(rid, includeIncoming: true, ct).ConfigureAwait(false);
        foreach (var r in refs)
        {
            if (!r.IsIncoming) continue;
            EntityRef? parent = null;
            if (entity.Kind == EntityKind.Task && r.RefType == CrossDbReferenceTypes.HistoryOwnsTask)
                parent = new EntityRef(EntityKind.History, r.Source.Id ?? "");
            else if (entity.Kind == EntityKind.Node && r.RefType == CrossDbReferenceTypes.CrossDbLink)
                parent = new EntityRef(EntityKind.Task, r.Source.Id ?? "");
            if (parent is null) continue;
            var pf = await ReadFlagAsync(parent.Value, ct).ConfigureAwait(false);
            if (pf is not null && !pf.Value.IsInherit()) return pf.Value;
            // 父层无显式标志 / Auto / Std → 继续向上继承（Node 没声明看 Task，Task 没声明看 History）
            var up = await ResolveUpAsync(parent.Value, visited, ct).ConfigureAwait(false);
            if (up is not null) return up;
        }
        return null;
    }

    /// <summary>取 task 的全部下游归属节点（cross_db_link 出边）。</summary>
    private async Task<IReadOnlyList<EntityRef>> GetDownstreamNodesAsync(EntityRef taskEntity, CancellationToken ct)
    {
        if (_store is null) return System.Array.Empty<EntityRef>();
        var refs = await _store.GetReferencesAsync(taskEntity.ToResourceIdentifier(), includeIncoming: false, ct).ConfigureAwait(false);
        var list = new List<EntityRef>();
        foreach (var r in refs)
        {
            if (r.RefType != CrossDbReferenceTypes.CrossDbLink) continue;
            if (TryParseNode(r.Target, out var node)) list.Add(node);
        }
        return list;
    }

    private static bool TryParseNode(ResourceIdentifier rid, out EntityRef node)
    {
        if (!string.Equals(rid.ResourceType, "node", System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rid.Id))
        {
            node = default;
            return false;
        }
        var dot = rid.Id.IndexOf('.');
        if (dot <= 0)
        {
            node = new EntityRef(EntityKind.Node, rid.Id);
            return true;
        }
        node = new EntityRef(EntityKind.Node, rid.Id[(dot + 1)..], rid.Id[..dot]);
        return true;
    }

    private static bool TryFromResourceIdentifier(ResourceIdentifier rid, out EntityRef entity)
    {
        var t = rid.ResourceType?.ToLowerInvariant();
        switch (t)
        {
            case "history": entity = new EntityRef(EntityKind.History, rid.Id ?? ""); return true;
            case "task": entity = new EntityRef(EntityKind.Task, rid.Id ?? ""); return true;
            case "node": return TryParseNode(rid, out entity);
            default: entity = default; return false;
        }
    }
}


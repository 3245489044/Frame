using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 重建执行器（UMF · R2）：把重建投影产出的 <c>(operation, request)</c> 归流到 <see cref="IMountPort"/>。
/// <para>
/// 判据载体 = 声明面结构事实：MountId 只能由**绑定表**反查（<see cref="MountBindingCatalog.ResolveByOperation"/>），
/// 0/多义 ⇒ 显式 <c>orphan-rebuild-action</c> 拒绝，禁静默回退旧写。
/// </para>
/// <para>
/// 删除方向：<c>unmount_*</c> 无 <c>[Mount]</c> 绑定，故先按同 edge 反查 ensure operation 再反查 MountId，
/// 写意图由 <see cref="MountIntent.Remove"/> 表达（门面内按 edge 反查 remove operation）。
/// </para>
/// </summary>
public sealed class MountRebuildExecutor
{
    private readonly IRelationPorts _ports;
    private readonly MountBindingCatalog _bindings;
    private readonly IMountPort _mounts;

    public MountRebuildExecutor(IRelationPorts ports, MountBindingCatalog bindings, IMountPort mounts)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));
    }

    /// <summary>执行一条重建动作（Origin=Rebuild，Trigger=rebuild.requested，窗口按 name+sourceKey 隔离）。</summary>
    public async Task<MountReceipt> ExecuteAsync(
        string name, string operationId, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(request);

        var correlation = $"rebuild:{name}:{request.SourceKey}";
        var stub = new MountRequest(string.Empty,
            new MountScope(string.Empty, request.SourceKey, request.Namespace, correlation),
            Array.Empty<MountTarget>());

        var operation = _ports.Operations.Resolve(operationId);
        if (operation is null)
            return MountReceipt.Rejected(stub, operationId, null, "undeclared-operation:" + operationId);

        // 删除方向没有 Mount 绑定 ⇒ 用同 edge 的 ensure operation 作为绑定锚点。
        var anchorOperation = operation.IsRemove
            ? _ports.Operations.ResolveByEdge(operation.Edge, remove: false)
            : operation;

        var mountId = anchorOperation is null ? null : _bindings.ResolveByOperation(anchorOperation.Id)?.MountId;
        if (mountId is null)
            return MountReceipt.Rejected(stub, operation.Id, operation.Edge, "orphan-rebuild-action:" + operationId);

        var scope = new MountScope(operation.Source, request.SourceKey, request.Namespace, correlation);
        var targets = request.Targets
            .Select(key => new MountTarget(operation.Target, key))
            .ToList();

        if (targets.Count == 0)
            return MountReceipt.NotApplicable(stub, operation.Id, operation.Edge, "no-targets");

        return await _mounts.ApplyAsync(new MountRequest(
            mountId,
            scope,
            targets,
            operation.IsRemove ? MountIntent.Remove : MountIntent.Ensure,
            MountOrigin.Rebuild,
            MountTriggers.RebuildRequested,
            Actor: request.Actor,
            CorrelationId: correlation), ct).ConfigureAwait(false);
    }
}

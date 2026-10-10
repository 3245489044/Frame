using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 写透守卫端口（唯一"写完联动"入口，对齐公理 DerivedDataAutoMaintenance / SingleImplementation，见 Agent/Axioms）：
/// 收敛「写完联动图+边」——包装 <see cref="INodeStore"/>，对**业务实体**（<see cref="NodeTypes.Node"/>）的
/// Put/Delete 自动联动同步图资源镜像（经 <see cref="ReferenceGrounding"/>）并依关系状态对账收敛边，
/// 其余判别类型（config / checkpoint / keymapping / input / output / event 等）直接透传不建镜像（避免图膨胀）。
/// 单一真源：镜像/边是否联动由 <see cref="IsBusinessNode"/> 白名单裁决，调用方不可按 per-call 开关越权。
/// <para>
/// 资源镜像 Id = <c>wf.node</c>（<see cref="NodeRelationFactory.NodeId"/>，workflowId 统一由
/// <see cref="WorkflowIds.Resolve(NodeRecord)"/> 解析），幂等（GetResourceAsync 已存在即跳过），删除级联沿用
/// <c>RemoveResourceAsync</c>。业务/执行代码不再手写 <c>EnsureNodeAsync</c>——写业务节点即自动落图镜像（写透 I1 不变式）。
/// </para>
/// </summary>
public sealed class WriteGuardPort : INodeStore
{
    private readonly INodeStore _inner;
    private readonly ReferenceGrounding _grounding;

    public WriteGuardPort(INodeStore inner, ReferenceGrounding grounding)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
    }

    public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);

    /// <summary>写入业务节点 → 写透图镜像（内容跟随 upsert，wf.node）；并依据节点关系状态对账收敛边。
    /// 非业务节点仅落基础节点库（不建图资源）。修改节点即自动同步 LiteDB 镜像与关系。
    /// workflowId 真源统一为 <see cref="WorkflowIds.Resolve(NodeRecord)"/>（节点 ThreadId 缺失回退默认值）——
    /// 与对账/重建侧同一权威，消除「写透注入」与「对账按 node.ThreadId」两套解析导致的图镜像 id 漂移（E-类风险）。</summary>
    public async Task<long> PutAsync(NodeRecord node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var id = await _inner.PutAsync(node, ct).ConfigureAwait(false);
        if (IsBusinessNode(node.NodeType))
        {
            var wf = WorkflowIds.Resolve(node);
            // ① 镜像内容跟随（upsert）：修改节点载荷/状态 → 图资源 symbol/metadata 同步更新（不再"仅确保存在"）。
            await _grounding.MirrorNodeAsync(wf, node, ct).ConfigureAwait(false);
            // ② 关系对账：权威边优先取自声明源（USharp 声明构建期确定，G3/G4）；缺省回退节点内关系状态（兼容读）。
            var edges = _grounding.AuthoritativeEdgesFor(wf, node);
            if (edges.Count > 0)
                await _grounding.WriteRelationsAsync(wf, node.NodeKey, edges, ct).ConfigureAwait(false);
        }
        return id;
    }

    public Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default) => _inner.GetAsync(nodeKey, ct);

    public Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
        long? afterId = null, int limit = 100, CancellationToken ct = default)
        => _inner.ListAsync(nodeType, threadId, afterId, limit, ct);

    /// <summary>删除业务节点 → 级联移除图镜像（RemoveResourceAsync 级联清边）；非业务节点仅删基础节点库。
    /// 与 PutAsync 白名单对称：先取节点判断类型，仅业务节点清镜像，避免误清非业务节点镜像。</summary>
    public async Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default)
    {
        var node = await _inner.GetAsync(nodeKey, ct).ConfigureAwait(false);
        var wasBusiness = node is not null && IsBusinessNode(node.NodeType);
        var removed = await _inner.DeleteAsync(nodeKey, ct).ConfigureAwait(false);
        if (removed && wasBusiness)
            await _grounding.RemoveNodeMirrorAsync(WorkflowIds.Resolve(node!), nodeKey, ct).ConfigureAwait(false);
        return removed;
    }

    /// <summary>批量删除业务节点 → 逐条级联移除图镜像（删除前取类型；DeleteMany 后 Get 不到节点）。</summary>
    public async Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default)
    {
        var keys = (nodeKeys ?? Enumerable.Empty<string>()).ToList();
        // 删除前先取待删键的类型（删除后无法回读），仅业务节点级联清图镜像。
        var business = new List<(string Key, string Wf)>();
        foreach (var key in keys)
        {
            var node = await _inner.GetAsync(key, ct).ConfigureAwait(false);
            if (node is not null && IsBusinessNode(node.NodeType))
                business.Add((key, WorkflowIds.Resolve(node)));
        }

        var removed = await _inner.DeleteManyAsync(keys, ct).ConfigureAwait(false);
        foreach (var (key, wf) in business)
            if (removed.Contains(key))
                await _grounding.RemoveNodeMirrorAsync(wf, key, ct).ConfigureAwait(false);
        return removed;
    }

    /// <summary>写透白名单：仅业务节点（NodeType=node）。内部记录（config/checkpoint/zone/keymapping）与
    /// 运行域事实（process：过程节点）不建图资源 —— process 判别值是「运行过程事实」，非业务实体，
    /// 写后既不入引用图、也不被引用重扫（ReferenceGrounding 按 NodeTypes.Node 全扫）纳入扫描面。
    /// 单一真源：判别类型经本守卫直写底层时，镜像与否由此白名单裁决，不依赖外壳。</summary>
    public static bool IsBusinessNode(string nodeType) => nodeType == NodeTypes.Node;
}

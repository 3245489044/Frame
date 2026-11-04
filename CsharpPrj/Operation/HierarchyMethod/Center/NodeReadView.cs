using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 节点读源单一视图（R4 canonical）：取代每步「ur_nodes 全量枚举 + 引用图解析」的跨库拼读。
/// 读多少由调用点显式声明（<see cref="NodeReadScope"/>）：空白 taskKey ⇒ 空集；传 taskKey ⇒ 按归属节点键过滤；
/// 未装配 task 键解析器却按 task 读 ⇒ 抛（禁 fail-open 读到全量）。全部 async 接口带 ct、存储读经
/// <see cref="CancellationToken"/> 传导。框架 LLM-free：task 键解析器以委托注入（由消费方
/// 桥接引用网关/守卫），本视图不直接依赖任一图存储类型。
/// </summary>
public sealed class NodeReadView
{
    private readonly INodeOutputStore _store;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? _resolveTaskNodeKeys;

    /// <param name="store">节点输出存储（ur_nodes 单真源读）。</param>
    /// <param name="resolveTaskNodeKeys">task → 归属节点键解析器（消费方桥接引用图 task 键，如 NodeRelationGateway）；null = 不做 task 过滤。</param>
    public NodeReadView(
        INodeOutputStore store,
        Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? resolveTaskNodeKeys = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolveTaskNodeKeys = resolveTaskNodeKeys;
    }

    /// <summary>
    /// 枚举本工作流 + task 归属的输出（单一读源）。空白 taskKey ⇒ 空集；传 taskKey ⇒ 按归属节点键过滤。
    /// 过滤结果为空（如首轮尚无归属边）→ 空集，不报错。需要全量请显式用 <see cref="ListOutputsAsync(string, NodeReadScope, CancellationToken)"/> + <see cref="NodeReadScope.Full"/>。
    /// </summary>
    public Task<IReadOnlyList<NodeOutput>> ListTaskOutputsAsync(
        string workflowId, string? taskKey, CancellationToken ct = default)
        => ListTaskOutputsAsync(workflowId, taskKey, _resolveTaskNodeKeys, ct);

    /// <summary>
    /// 同三参重载，但解析器经调用方显式注入。
    /// 供需要保留「图读(引用)解析 vs ur_nodes 枚举」两段诊断边界（在解析器内打点）的消费方使用：本方法先调解析器、
    /// 后枚举 ur_nodes，消费方可在解析器内/返回后各打一条以精确定位读锁卡点归属，而不牺牲读源收敛。
    /// <para>语义统一收敛到 <see cref="ListOutputsAsync(string, NodeReadScope, Func{string, string, CancellationToken, Task{IReadOnlyCollection{string}}}?, CancellationToken)"/>：
    /// 空白 taskKey ⇒ 空集（不降级全量）；非空 taskKey ⇒ ByTask（解析器未装配即抛，禁 fail-open 读到跨 task 全量）。</para>
    /// </summary>
    public async Task<IReadOnlyList<NodeOutput>> ListTaskOutputsAsync(
        string workflowId, string? taskKey,
        Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? resolveTaskNodeKeys,
        CancellationToken ct)
    {
        // 身份缺失（空白键）⇒ 空集：不降级为全量枚举（ERR-01 跨 task 读回归的唯一入口）。
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<NodeOutput>();
        // 统一走 ByTask：解析器未装配 ⇒ 抛（ERR-02，禁止把「无法按任务隔离」降级为「读全部 task」）。
        return await ListOutputsAsync(workflowId, NodeReadScope.ByTask(taskKey), resolveTaskNodeKeys, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 范围化读取（F3 唯一入口）：<see cref="NodeReadScope"/> 三形态由调用点显式声明，
    /// 禁再出现「空 key ⇒ 全量」的隐式落点（ERR-01）。
    /// </summary>
    public Task<IReadOnlyList<NodeOutput>> ListOutputsAsync(
        string workflowId, NodeReadScope scope, CancellationToken ct = default)
        => ListOutputsAsync(workflowId, scope, _resolveTaskNodeKeys, ct);

    /// <summary>
    /// 范围化读取（解析器经调用方显式注入）：
    /// <see cref="NodeReadScopeKind.Full"/> ⇒ 全量枚举；<see cref="NodeReadScopeKind.ByTask"/> ⇒ 解析归属键后有界读取；
    /// <see cref="NodeReadScopeKind.ByKeys"/> ⇒ 按键集合有界读取（键集即上界）。
    /// <para>ByTask 未装配解析器 ⇒ 抛（接线缺口不得表现为「读到全量」）；直跑/单测请显式用 <see cref="NodeReadScope.Full"/>。</para>
    /// </summary>
    public async Task<IReadOnlyList<NodeOutput>> ListOutputsAsync(
        string workflowId, NodeReadScope scope,
        Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? resolveTaskNodeKeys,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(workflowId);

        switch (scope.Kind)
        {
            case NodeReadScopeKind.Full:
                return await _store.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);

            case NodeReadScopeKind.ByTask:
                if (resolveTaskNodeKeys is null)
                    throw new InvalidOperationException(
                        "NodeReadScope.ByTask 需要 task→归属节点键解析器（未装配即接线缺口）；直跑/单测请显式使用 NodeReadScope.Full。");
                var taskNodeKeys = await resolveTaskNodeKeys(scope.TaskKey!, workflowId, ct).ConfigureAwait(false);
                return await ReadByKeysAsync(workflowId, taskNodeKeys, ct).ConfigureAwait(false);

            default:
                return await ReadByKeysAsync(workflowId, scope.NodeKeys, ct).ConfigureAwait(false);
        }
    }

    /// <summary>按键有界读取（复用单节点读端口，不再「全量枚举 + 内存过滤」；键集为空 ⇒ 空集）。</summary>
    private async Task<IReadOnlyList<NodeOutput>> ReadByKeysAsync(
        string workflowId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct)
    {
        if (nodeKeys.Count == 0) return Array.Empty<NodeOutput>();

        var outputs = new List<NodeOutput>(nodeKeys.Count);
        foreach (var nodeKey in nodeKeys)
        {
            var output = await _store.TryGetNodeOutputAsync(workflowId, nodeKey, ct).ConfigureAwait(false);
            if (output is not null) outputs.Add(output);
        }

        return outputs;
    }
}

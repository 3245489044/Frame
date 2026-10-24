using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 入参候选源映射（R4 读源收敛产物）：qualified `{node}.{port}` 恒有，裸 `{port}` 仅在尚未被任一节点输出占用时加入
/// （避免跨节点裸端口互覆盖，溯源随之填充，保证 NodeToNode 组合边一致）。与 NodeExecutionService.ResolveInputsAsync
/// 的源构建语义一致，但落在框架中立层，供任何消费方经统一读源视图消费。
/// </summary>
public sealed record NodeSourceMap(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyDictionary<string, (string Node, string Port)> Provenance);

/// <summary>
/// 节点读源单一视图（R4 canonical）：取代每步「ur_nodes 全量枚举 + 引用图解析」的跨库拼读。
/// 只读本工作流 + task 归属的节点输出（task 过滤根除跨 task 污染）；无 taskKey / 未装配 task 键解析器 → 全量枚举（兼容直跑）。
/// 全部 async 接口带 ct、存储读经 <see cref="CancellationToken"/> 传导。框架 LLM-free：task 键解析器以委托注入（由消费方
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
    /// 枚举本工作流 + task 归属的输出（单一读源）。传 taskKey 且装配解析器 → 按归属节点键过滤；否则全量（兼容直跑）。
    /// 过滤结果为空（如首轮尚无归属边）→ 空集，不报错。
    /// </summary>
    public Task<IReadOnlyList<NodeOutput>> ListTaskOutputsAsync(
        string workflowId, string? taskKey, CancellationToken ct = default)
        => ListTaskOutputsAsync(workflowId, taskKey, _resolveTaskNodeKeys, ct);

    /// <summary>
    /// 同三参重载，但解析器经调用方显式注入。
    /// 供需要保留「图读(引用)解析 vs ur_nodes 枚举」两段诊断边界（在解析器内打点）的消费方使用：本方法先调解析器、
    /// 后枚举 ur_nodes，消费方可在解析器内/返回后各打一条以精确定位读锁卡点归属，而不牺牲读源收敛。
    /// </summary>
    public async Task<IReadOnlyList<NodeOutput>> ListTaskOutputsAsync(
        string workflowId, string? taskKey,
        Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? resolveTaskNodeKeys,
        CancellationToken ct)
    {
        // 身份缺失（空白键）⇒ 空集：不再降级为全量枚举（ERR-01 跨 task 读回归的唯一入口）。
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<NodeOutput>();
        // 解析器未装配 ⇒ 全量（既有兼容面；新调用点应改用 NodeReadScope 显式声明读量）。
        if (resolveTaskNodeKeys is null)
            return await _store.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);

        var taskNodeKeys = await resolveTaskNodeKeys(taskKey, workflowId, ct).ConfigureAwait(false);
        if (taskNodeKeys.Count == 0) return Array.Empty<NodeOutput>();

        var all = await _store.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);
        return all.Where(o => taskNodeKeys.Contains(o.NodeKey)).ToList();
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

    /// <summary>由输出列表构建入参候选源映射（qualified + 裸端口、含溯源）；纯函数无 IO。</summary>
    public static NodeSourceMap BuildSourceMap(IReadOnlyList<NodeOutput> outputs)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var provenance = new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            foreach (var (port, value) in output.OutputPorts)
            {
                if (value is null) continue;
                var qual = $"{output.NodeKey}.{port}";
                values[qual] = value;
                provenance[qual] = (output.NodeKey, port);
                if (!provenance.ContainsKey(port))
                {
                    values[port] = value;
                    provenance[port] = (output.NodeKey, port);
                }
            }
        }
        return new NodeSourceMap(values, provenance);
    }
}

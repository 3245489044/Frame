using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Operation.HierarchyMethod.Nodes;
using Share.HierarchySupport;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点入参快照（中立形态）：入参字典 + 溯源（参数键 ← 上游节点输出端口，供图 NodeToNode 组合边持久化）。
/// 自 Agent 侧 NodeInputResolution 平移，去 Agent 类型依赖。
/// </summary>
public sealed record NodeInputSnapshot(
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyDictionary<string, (string Node, string Port)> Provenance);

/// <summary>
/// 一轮节点编排的共享上下文（自 Agent RoundPipelineContext 平移，中立化）：
/// 承载编排数据空间（Vars，<see cref="IDataSpace"/>）、会话/取消/错误归集，以及步骤产物暂存与持久化登记。
/// 不含 Agent 专属状态适配器（StateContextAdapter）——状态外置由 Agent 适配器在装配期注入 Vars 来源
/// （如 StateCenter.Transient 层视图或进程内内存 DataSpace）。
/// </summary>
public sealed class NodeRoundContext
{
    public NodeRoundContext(IDataSpace vars)
    {
        Vars = vars;
    }

    /// <summary>编排数据空间（步骤间共享；来源由装配期注入，如 StateCenter.Transient 层视图或内存 DataSpace）。</summary>
    public IDataSpace Vars { get; }

    /// <summary>本轮取消令牌（每次运行经编排器注入）。</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>本轮会话作用域（taskKey，供 task→node 归属边与读侧 task 过滤）。由编排器注入。</summary>
    public string? SessionId { get; set; }

    /// <summary>本轮轮序号（1-based；= 轮级关联键 <c>会话键#R&lt;n&gt;</c> 的 n，标识单源见 Agent 侧 <c>FactIds.Round</c>／<c>AgentInstance.RoundKey</c>；旧形态 <c>runId+"R"+n</c> 已废除）。
    /// 本层只当不透明整数承载，不感知类型；0 = 未注入（直跑/非 Agent 路径，埋点方据此跳过观测）。</summary>
    public int RoundNo { get; set; }

    /// <summary>
    /// 透明 run 作用域槽（跨层中性透传）：由宿主（Agent 运行持有方）每轮填充当前 run 作用域对象，
    /// 供本节点编排期间到达深层函数/过滤器的显式承载——AsyncLocal 去除后的替代通道。
    /// 本层（Operation.HierarchyMethod，CsharpPrj）只当不透明 object 承载，不感知类型、不触发反依赖；
    /// 无宿主填充 = null（直跑 / 非 Agent 路径零影响，维持既有 no-op 语义）。
    /// </summary>
    public object? RunScopeSlot { get; set; }

    /// <summary>本轮非取消错误信息（步骤执行异常归集，无异常为 null）。</summary>
    public string? Error { get; set; }

    /// <summary>本轮错误分类（强类型归因；无异常为 null）。</summary>
    public ErrorClass? ErrorClass { get; set; }

    /// <summary>首个出错步骤名（错误归因；无异常为 null）。</summary>
    public string? ErrorStep { get; set; }

    // ---- 步骤产物暂存（供 onAfterStep 横切持久化读取；步骤体只做函数执行 + 内存写，落库横切后置） ----
    // 键 = 稳定节点 ID（RoundStep.EffectiveNodeId / NodeDefinition.NodeKey），M1 收敛：同名函数多次出现不再覆盖。
    private readonly Dictionary<string, NodeDefinition> _stepNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, object?>> _stepProps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeInputSnapshot> _stepInputs = new(StringComparer.Ordinal);

    // ---- 已持久化节点端口登记（供终态信封补漏判定：横切已写则不再补写，消除 R1 双写） ----
    private readonly HashSet<string> _persistedPorts = new(StringComparer.Ordinal);

    /// <summary>登记某节点端口已由步骤横切持久化（Persist 成功后调用；终态信封据此跳过重复写）。
    /// R1 收敛：同一轮内同一节点端口至多登记一次 —— 重复登记即「横切 + 终态信封」双写告警（Debug.Assert 开发期拦截）。</summary>
    public void MarkPersistedPort(string nodeKey, string port)
        => Debug.Assert(_persistedPorts.Add($"{nodeKey}.{port}"),
            $"[R1] 节点端口 {nodeKey}.{port} 在本轮被重复登记已持久化（双写告警）");

    /// <summary>查询某节点端口是否已由步骤横切持久化（false = 需终态信封补写兜底，如 guard_output 被 CanExecute 跳过）。</summary>
    public bool IsPortPersisted(string nodeKey, string port)
        => _persistedPorts.Contains($"{nodeKey}.{port}");

    /// <summary>记录当前步骤执行产物 + 入参溯源（步骤体调用；供 onAfterStep 持久化横切消费，消除溯源在落库后置时的丢失）。</summary>
    public void RecordStep(string stepName, NodeDefinition node, Dictionary<string, object?> props, NodeInputSnapshot input)
    {
        _stepNodes[stepName] = node;
        _stepProps[stepName] = props;
        _stepInputs[stepName] = input;
    }

    /// <summary>取步骤产物 + 入参溯源；步骤未执行/已无记录返回 false。</summary>
    public bool TryGetStep(string stepName, out NodeDefinition? node, out IReadOnlyDictionary<string, object?> props, out NodeInputSnapshot? input)
    {
        node = _stepNodes.TryGetValue(stepName, out var n) ? n : null;
        props = _stepProps.TryGetValue(stepName, out var p) ? p : null!;
        input = _stepInputs.TryGetValue(stepName, out var i) ? i : null;
        return node != null && props != null && input != null;
    }

    /// <summary>枚举本轮全部已执行步骤快照（节点 + 端口产物 + 入参溯源）。顺序即步骤执行序（_stepNodes 插入序）。</summary>
    public IReadOnlyList<(string StepName, NodeDefinition Node, IReadOnlyDictionary<string, object?> Props, NodeInputSnapshot Input)> EnumerateSteps()
    {
        var list = new List<(string, NodeDefinition, IReadOnlyDictionary<string, object?>, NodeInputSnapshot)>();
        foreach (var name in _stepNodes.Keys)
        {
            if (!_stepProps.TryGetValue(name, out var props) ||
                !_stepInputs.TryGetValue(name, out var input) ||
                input is null) continue;
            list.Add((name, _stepNodes[name], props, input));
        }
        return list;
    }
}

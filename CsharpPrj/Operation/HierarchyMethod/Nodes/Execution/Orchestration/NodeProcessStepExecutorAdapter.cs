using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 端口适配器：把 NodeProcess 执行端口（nodeKey, mergedParameters）映射回节点执行器的 <see cref="RoundStep"/> 路径。
/// <para>
/// 自 Agent 侧 <c>NodeProcessExecutorAdapter</c> 下沉框架；关键改造：本轮上下文由 <see cref="NodeRoundContext"/> **显式注入**
/// （原经 Agent 侧 <c>RunContext</c> AsyncLocal 读取），使框架侧不再依赖宿主的环境上下文通道（作用域通道单份）。
/// </para>
/// <para>
/// 横切持久化**不在此触发**：由编排器 <see cref="INodeExecutorHooks.OnAfterStepAsync"/> 统一承担（原 NodeProcess 后端经
/// 本适配器的 afterStep 回调触发 → 合一后并入编排器，避免双写）。</para>
/// </summary>
public sealed class NodeProcessStepExecutorAdapter : Share.StateManagement.INodeExecutor
{
    private readonly IReadOnlyDictionary<string, RoundStep> _byNode;
    private readonly Func<RoundStep, NodeRoundContext, KeyMappingConfig?, CancellationToken, Task<NodeExecutionRecord?>> _execute;
    private readonly NodeRoundContext _ctx;
    private readonly KeyMappingConfig? _globalMapping;

    /// <param name="spec">本轮工作流（提供 nodeKey → RoundStep 反查与全局键映射）。</param>
    /// <param name="ctx">本轮编排上下文（显式注入，取代环境上下文读取）。</param>
    /// <param name="execute">实际执行体（生产装配 = 节点执行器 <see cref="INodeExecutor.ExecuteStepAsync"/>）。</param>
    public NodeProcessStepExecutorAdapter(
        RoundSpec spec,
        NodeRoundContext ctx,
        Func<RoundStep, NodeRoundContext, KeyMappingConfig?, CancellationToken, Task<NodeExecutionRecord?>> execute)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(execute);
        _byNode = spec.Steps.ToDictionary(s => s.EffectiveNodeId, StringComparer.Ordinal);
        _globalMapping = spec.GlobalKeyMapping;
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _execute = execute;
    }

    /// <summary>节点执行器直装构造：把 <see cref="INodeExecutor.ExecuteStepAsync"/> 适配为内部执行体（声明式装配免 lambda）。</summary>
    public NodeProcessStepExecutorAdapter(
        RoundSpec spec,
        NodeRoundContext ctx,
        INodeExecutor executor)
        : this(spec, ctx, executor is null
            ? throw new ArgumentNullException(nameof(executor))
            : (step, c, gm, ct) => executor.ExecuteStepAsync(step, c, gm, ct))
    {
    }

    public async Task<Share.StateManagement.NodeExecutionResult> ExecuteAsync(
        string nodeKey, IDictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        if (!_byNode.TryGetValue(nodeKey, out var step))
            return new Share.StateManagement.NodeExecutionResult { Success = false };

        var record = await _execute(step, _ctx, _globalMapping, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return new Share.StateManagement.NodeExecutionResult { Success = false };

        var props = record.OutputPorts
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);

        return new Share.StateManagement.NodeExecutionResult
        {
            Success = true,
            Result = record,
            OutputParameters = props,
        };
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// NodeProcess 执行状态机派发器：把 <see cref="INodeStepDispatcher"/> 的「如何驱动单节点」实现为
/// 「经 <see cref="NodeExecutionStage"/> 状态机（节点登记 → 信号量 → 参数合并 → 适配执行 → 完成/失败标记）」。
/// <para>
/// 原 Agent 侧 <c>NodeProcessRoundBackend</c> 的整份轮骨架（seed/裁决/harvest）已弃用——轮骨架唯一权威为
/// <see cref="NodeGraphOrchestrator"/>；本类只保留其**证据性差异**（执行状态机登记），经该注入点表达。
/// </para>
/// <para>
/// 与执行状态机一致，逐节点执行经本适配器纳入状态机管理；横切持久化仍由编排器 onAfterStep 承担（单次，不重复）。
/// </para>
/// </summary>
public sealed class NodeExecutionStageDispatcher : INodeStepDispatcher
{
    private readonly INodeExecutor _executor;
    private readonly IDiagnosticsSink? _diagnostics;
    private NodeExecutionStage? _stage;
    private string? _executionId;
    private RoundSpec? _spec;

    /// <param name="executor">节点执行器（生产 = 节点执行服务/步骤调用器；测试桩亦可）。</param>
    /// <param name="diagnostics">诊断可见化端口（缺省空实现）。</param>
    public NodeExecutionStageDispatcher(INodeExecutor executor, IDiagnosticsSink? diagnostics = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _diagnostics = diagnostics;
    }

    /// <summary>本轮执行 id（对齐会话，避免跨轮状态串扰）。</summary>
    public string? ExecutionId => _executionId;

    /// <inheritdoc />
    public void BeginRound(RoundSpec spec, NodeRoundContext ctx)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        // 每轮独立 NodeProcess 执行状态机（executionId 对齐本轮会话，避免跨轮状态串扰）。
        _spec = spec;
        _executionId = $"{spec.Name}:{ctx.SessionId ?? "anon"}";
        _stage = new NodeExecutionStage();
        _stage.StartExecution(_executionId);
        _diagnostics?.Report("NodeExecutionStage_dispatcher_start", new { executionId = _executionId, steps = spec.Steps.Count });
    }

    /// <inheritdoc />
    public async Task<NodeExecutionRecord?> DispatchAsync(
        RoundStep step, NodeRoundContext ctx, KeyMappingConfig? globalMapping, CancellationToken ct)
    {
        if (_stage is null || _executionId is null || _spec is null)
            throw new InvalidOperationException("NodeExecutionStageDispatcher.BeginRound 未调用（每轮须先登记执行）。");

        var adapter = new NodeProcessStepExecutorAdapter(
            _spec,
            ctx,
            (s, c, g, t) => _executor.ExecuteStepAsync(s, c, g ?? globalMapping, t));

        // NodeProcess 执行内核：登记节点 → 信号量 → MergeParameters → adapter.ExecuteAsync → 状态机登记完成/失败。
        var result = await _stage.ExecuteNodeWithParametersAsync(
            _executionId,
            step.EffectiveNodeId,
            adapter,
            new Dictionary<string, object>(StringComparer.Ordinal),
            ct).ConfigureAwait(false);

        if (result.Success)
            return result.Result as NodeExecutionRecord; // Success 仅在 adapter 拿到 record 后置真

        // 失败：取消优先上抛（不归为普通错误）；真实异常按原类型上抛（保留 ErrorClassifier 的分类口径）；Error=null = 跳过。
        if (ct.IsCancellationRequested)
            ct.ThrowIfCancellationRequested();

        if (result.Error is { } ex)
        {
            _diagnostics?.Report("NodeExecutionStage_dispatcher_stepFailed", new { node = step.EffectiveNodeId, error = ex.Message });
            ExceptionDispatchInfo.Capture(ex).Throw();
        }

        return null;
    }
}

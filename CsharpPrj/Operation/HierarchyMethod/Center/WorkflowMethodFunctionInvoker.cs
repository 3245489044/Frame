using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using WorkFlowMethod.Usage;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// WorkFlowMethod 函数调用适配器 —— 唯一 canonical 实现（R2 收敛 / SingleImplementation / TH-12）。
/// 收敛 Agent.Core 与 Produce.Core 各自平行的「WorkflowMethodFunctionInvoker」为单一实现：包住
/// <see cref="WorkFlowMethodUsage.CallFunctionAsync"/>，参数一律经 FunctionContext 语义（构造 <see cref="WfContext"/>，
/// 只传命名参数字典），返回注册函数原始返回值。框架 LLM-free：仅把「按名调用注册函数」中立化为 <see cref="INodeFunctionInvoker"/> 端口实现。
/// <para>行为与旧双实现一致：Agent 旧实现 <c>new WorkflowMethodFunctionInvoker(workflowId)</c>（executionId={workflowId}_{guid}）；Produce 旧实现
/// 经 <c>executionIdPrefix:"produce"</c> 重建。ctx.workflowId/nodeKey 仅诊断用，注册函数实际只消费命名参数。</para>
/// </summary>
public sealed class WorkflowMethodFunctionInvoker : INodeFunctionInvoker
{
    private readonly string _workflowId;
    private readonly string _idPrefix;

    public WorkflowMethodFunctionInvoker(
        string? defaultWorkflowId = null,
        string? executionIdPrefix = null)
    {
        _workflowId = string.IsNullOrWhiteSpace(defaultWorkflowId)
            ? NodeKeys.DefaultWorkflowId : defaultWorkflowId;
        _idPrefix = string.IsNullOrWhiteSpace(executionIdPrefix) ? _workflowId : executionIdPrefix;
    }

    public async Task<object?> InvokeAsync(
        string funcName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 构造 WorkflowContext（FunctionContext 语义）：只传命名参数字典（去 null 值，对齐旧 input.Parameters 非空语义）；
        // executionId 唯一性用 Guid 保证；nodeKey 回退 funcName（注册函数主要消费 Parameters，nodeKey/workflowId 仅诊断）。
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in parameters)
            if (kv.Value != null) dict[kv.Key] = kv.Value;

        var ctx = new WfContext(
            parameters: dict,
            data: new Dictionary<string, object>(dict, StringComparer.Ordinal),
            executionId: $"{_idPrefix}_{Guid.NewGuid():N}",
            nodeKey: funcName,
            workflowId: _workflowId);

        return await WorkFlowMethodUsage.CallFunctionAsync(funcName, ctx).ConfigureAwait(false);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点执行横切钩子（对应 StaticAsyncPipeline 的 onAfterStep / onStepError）：编排器在每步执行后/出错时回调。
/// Agent 侧实现为持久化横切（ur_nodes + SQL + 图关系）与错误强类型归因；未装配 → 无操作（增强路径 no-op）。
/// 框架本体零 LLM。
/// </summary>
public interface INodeExecutorHooks
{
    /// <summary>步骤执行成功后横切（持久化节点输出/元数据，经 <paramref name="ctx"/> 检索步骤产物）。
    /// 失败由实现侧单独归类、不翻转业务 ok。</summary>
    Task OnAfterStepAsync(NodeRoundContext ctx, NodeExecutionRecord record, CancellationToken ct);

    /// <summary>步骤执行异常横切（错误强类型归因）。OperationCanceledException 应由编排器先处理、不回调本方法。</summary>
    Task OnStepErrorAsync(NodeRoundContext ctx, NodeExecutionRecord record, Exception ex, CancellationToken ct);
}

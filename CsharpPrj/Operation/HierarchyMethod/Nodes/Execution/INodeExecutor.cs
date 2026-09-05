using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点执行器端口 —— 编排器把「执行单个步骤」委托给注入的实现（Agent 侧为 RoundStepInvoker 直接实现：
/// BuildNode → ResolveInputs → 调函数 → RecordStep，含 guard_input echo / return spec 等步骤横切）。
/// 返回该步骤的节点执行快照；返回 null = 步骤未执行（如条件门控 CanExecute 跳过）。
/// 未装配 → 编排器无法执行（构造期必须注入）。
/// </summary>
public interface INodeExecutor
{
    /// <summary>执行单个步骤，返回节点执行快照；步骤未执行返回 null。</summary>
    /// <param name="globalMapping">工作流全局键映射（spec.GlobalKeyMapping），供入参解析合并。</param>
    Task<NodeExecutionRecord?> ExecuteStepAsync(
        RoundStep step,
        NodeRoundContext ctx,
        KeyMappingConfig? globalMapping = null,
        CancellationToken ct = default);
}

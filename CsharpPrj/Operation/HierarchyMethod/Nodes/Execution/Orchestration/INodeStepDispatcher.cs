using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 单步派发端口（轮骨架内的「如何驱动单节点」接缝）：
/// 默认语义 = 直接委托 <see cref="INodeExecutor"/>；NodeProcess 语义 = 经执行状态机（登记/完成/失败标记）驱动。
/// <para>
/// 引入背景（S4 轮骨架合一）：原「<see cref="NodeGraphOrchestrator"/> 线性遍历」与「NodeProcess 后端」是
/// **同一轮语义骨架的两份实现**（seed → 逐节点执行 + 横切 → 取消/错误归集 → 成功准则裁决 → harvest → 结果），
/// 差异仅在逐节点是否经 <c>NodeExecutionStage</c> 状态机。现把该差异收敛为本端口的一个实现，
/// 框架层不再出现第二份 seed/裁决/harvest 代码。
/// </para>
/// </summary>
public interface INodeStepDispatcher
{
    /// <summary>一轮开始钩子：需要轮级登记的实现（如 NodeProcess 执行状态机 StartExecution）在此初始化。
    /// 缺省 no-op（直接委托执行的实现无需实现本方法）。</summary>
    void BeginRound(RoundSpec spec, NodeRoundContext ctx)
    {
    }

    /// <summary>派发单个步骤，返回节点执行快照；步骤未执行（跳过）返回 null；失败经异常上抛（由编排器统一归集）。</summary>
    Task<NodeExecutionRecord?> DispatchAsync(
        RoundStep step,
        NodeRoundContext ctx,
        KeyMappingConfig? globalMapping,
        CancellationToken ct);
}

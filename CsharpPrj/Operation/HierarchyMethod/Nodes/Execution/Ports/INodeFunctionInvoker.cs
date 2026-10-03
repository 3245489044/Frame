using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 函数调用端口 —— 解耦「引擎调用注册函数」与「具体注册表（WorkFlowMethod）」。
/// 参数一律经 FunctionContext 语义（只传命名参数字典，不暴露具体执行上下文类型），
/// 返回注册函数的**原始返回值**（<see cref="object"/>）；节点层负责结果端口提取
/// （定向提取需节点 <see cref="Operation.HierarchyMethod.Nodes.NodeDefinition.ResultKeys"/>，属节点层职责，不沉入本端口）。
/// Agent 侧以 WorkFlowMethod 适配器实现本端口；未装配 → 默认适配器兜底。框架本体零 LLM。
/// </summary>
public interface INodeFunctionInvoker
{
    /// <summary>按命名参数调用注册函数，返回原始结果（null = handler 合法返回 null / 解包无值）。</summary>
    Task<object?> InvokeAsync(
        string funcName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct);
}

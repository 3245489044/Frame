using System;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// LLM 执行端统一端口（中立契约，LLM 无关）—— 本地 / 真实 daemon / answer 收敛为同一执行契约。
/// 消费中立 <see cref="NodeExecutionRequest"/>，产出中立 <see cref="NodeExecutionResult"/>；
/// 具体执行器（ClineDaemonEndpoint/ClineLocalEndpoint 等）作为本接口实现，把中立请求还原为其强类型语义。
/// 自 Agent.Modules 下沉：接口本体归框架，Agent 侧执行器仅作可注入适配器。
/// </summary>
public interface IExecutionEndpoint
{
    /// <summary>执行端名（cline-daemon / cline-local / stateless-answer）。</summary>
    string Name { get; }

    /// <summary>执行一次代理任务，返回统一中立结果。</summary>
    /// <param name="request">中立执行请求（分层消息 + 空间边界 + 富化扩展槽）。</param>
    /// <param name="ct">取消令牌（统一贯穿）。</param>
    /// <param name="progress">心跳进度（可选）；任何推进证据都应经此上报。</param>
    Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionRequest request,
        CancellationToken ct = default,
        IProgress<EndpointProgress>? progress = null);
}

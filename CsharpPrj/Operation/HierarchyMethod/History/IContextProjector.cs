using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 上下文投影器（History 层）：「上下文组装」的粘合点 —— 把历史回喂（<see cref="IHistoryProjector.BuildContextAsync"/> 产出的
/// <see cref="NodeRoundInput"/>）+ 本轮数据空间（vars）投影为一次中立执行请求 <see cref="NodeExecutionRequest"/>，
/// 交给执行端点适配器（Agent 侧 cline_execute/EndpointResolver）执行。
/// LLM 专属投影（L0/L1/L2 提示词分层组装、L1 taskKey 缓存、SK 生成）由 Agent 适配器实现本端口提供；
/// 框架本体只定义「历史 + 本轮上下文 → 中立执行请求」的投影端口，零 LLM。未装配 → 消费方以 vars 直接构造请求（no-op 投影）。
/// </summary>
public interface IContextProjector
{
    /// <summary>
    /// 把累积历史上下文（<paramref name="input"/>，来自 <see cref="IHistoryProjector.BuildContextAsync"/>）+ 本轮数据空间
    /// （<paramref name="vars"/>）投影为一次中立执行请求。Messages 键约定 <c>"system"</c> + <c>"user1"/"user2"…</c>（数据形态，不含协议知识）。
    /// </summary>
    Task<NodeExecutionRequest> ProjectAsync(
        NodeRoundInput input,
        IReadOnlyDictionary<string, object?> vars,
        CancellationToken ct = default);
}

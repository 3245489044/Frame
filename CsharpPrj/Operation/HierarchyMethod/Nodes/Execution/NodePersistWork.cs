using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点持久化工作项（Outbox 载荷）：捕获一次步骤后持久化所需全部数据（节点 + 产物端口 + 入参快照 + 任务键），
/// 供后台 writer 延迟落库。对齐架构根治方案 R2（Outbox 解耦）：执行热路径只 append（内存 O(1)），
/// 跨介质落库由 <see cref="Modules.Threading.Streaming.OutboxWriter{TWork}"/> 后台消费，绝不阻塞主循环。
/// 自 Agent 侧同记录平移（入参记录名统一为框架 <see cref="NodeInputSnapshot"/>）。
/// </summary>
public sealed record NodePersistWork(
    NodeDefinition Node,
    IReadOnlyDictionary<string, object?> Props,
    NodeInputSnapshot Input,
    string? TaskKey);

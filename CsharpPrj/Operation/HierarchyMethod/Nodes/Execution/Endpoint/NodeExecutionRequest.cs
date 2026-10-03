using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 中立执行请求（跨 Nodes/Tasks/History 三层共享，LLM 无关）：分层消息 + 会话/空间边界 + 富化扩展槽。
/// 承载「执行一段代理任务」的通用输入；具体执行器（Cline/本地/answer）作为 <see cref="IExecutionEndpoint"/>
/// 实现消费本请求，把消息与富化字段还原为其强类型语义（Agent 侧经映射助手转换）。
/// </summary>
/// <remarks>
/// Messages 字典键约定（数据形态，不携带协议知识）：<c>"system"</c>（系统底座，L0）+ <c>"user1"/"user2"…</c>（有序指令段）。
/// 执行器专属强类型字段（Provider/Model/BaseUrl/ClientType/Round 等）一律经 <see cref="Extras"/> 承载，不进入本契约字段。
/// </remarks>
public sealed record NodeExecutionRequest(
    IReadOnlyDictionary<string, string?> Messages,
    string? SessionId = null,
    string? WorkingDirectory = null,
    string? DataDirectory = null,
    IReadOnlyDictionary<string, object?>? Extras = null);

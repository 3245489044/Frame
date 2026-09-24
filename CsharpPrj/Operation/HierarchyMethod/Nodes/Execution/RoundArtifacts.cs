using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮次产物（runner 输出归一形状，CP-1）—— 折叠链路与工作流链路**同形产出**，堵住「折叠 runner Nodes 恒空 →
/// 本轮 ur_nodes 零行」的断链。
/// <para>
/// 三层落点分工（见《会话轮次产物分层》§2.1）：<see cref="Nodes"/> = 全量层（ur_nodes，任务面板真源）；
/// <see cref="TerminalNodeKey"/>/<see cref="TerminalText"/> = 入史层依据（终答默认标记）；
/// <see cref="Archives"/> = 归档层（子执行端原始轮次正文，任务面板详情/resume 源）。
/// </para>
/// </summary>
/// <param name="TaskKey">会话/任务键（节点归属与入史容器键）。</param>
/// <param name="Round">轮次号（复用轮次治理既有 Id 文法，禁新造编号）。</param>
/// <param name="Nodes">本轮全量节点产出（L-A：每步一个节点）。</param>
/// <param name="TerminalNodeKey">终答来源节点键（null = 未归因；入史层据此默认标 Terminal）。</param>
/// <param name="TerminalText">终答文本（null = 无终答）。</param>
/// <param name="Archives">归档层条目（可选；空 = 本轮无归档）。</param>
public sealed record RoundArtifacts(
    string TaskKey,
    int Round,
    IReadOnlyList<NodeExecutionRecord> Nodes,
    string? TerminalNodeKey = null,
    string? TerminalText = null,
    IReadOnlyList<RoundArchiveItem>? Archives = null);

/// <summary>
/// 归档层条目（中立形态）：子执行端某一轮的原始正文全文（如 cline messages.json），
/// 归档为 <c>NodeType=cline_messages</c> 节点（任务面板「展开详情」/resume 回灌/审计源）。
/// <para>框架层中立：不携带任何具体执行端词表，执行端名由 Agent 侧装载进 <see cref="SessionId"/> 语义。</para>
/// </summary>
/// <param name="SessionId">子执行端会话键（归档节点键命名空间）。</param>
/// <param name="Round">该子会话的轮次号。</param>
/// <param name="MessagesJson">原始轮次正文全文。</param>
/// <param name="Status">归档状态（completed/faulted/partial）。</param>
public sealed record RoundArchiveItem(
    string SessionId,
    int Round,
    string MessagesJson,
    string Status = "completed");

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 轮次键控**原文全量归档**端口（通用）。
/// 每轮按 <c>(会话容器, round)</c> 幂等重归档（同轮覆盖，语义等价于按轮 upsert）。
/// <para>
/// 语义与领域无关：载荷为**不透明原文**（如 SDK 会话 messages.json 全文），形态由调用方负责；
/// 归档键 = 框架会话键（会话名 / taskKey）。归档与入史层（标记节点投影会话消息）职责分离——
/// 原文全量层不得混入 LLM 上下文（否则即 prompt 膨胀的直接来源）。
/// </para>
/// </summary>
public interface IRoundContentArchive
{
    /// <summary>归档一轮原文（按 (会话容器, round) 幂等 upsert）。</summary>
    Task ArchiveAsync(string sessionId, int round, string messagesJson, string status, CancellationToken ct = default);

    /// <summary>读取指定轮次原文（回放/对账/再材料化用）。</summary>
    Task<string?> LoadAsync(string sessionId, int round, CancellationToken ct = default);

    /// <summary>读取会话最近一轮原文（= 已归档的最大 round）。</summary>
    Task<string?> LoadLatestAsync(string sessionId, CancellationToken ct = default);

    /// <summary>推导下一归档轮次（已归档最大 + 1；无归档则为 1）。</summary>
    Task<int> NextRoundAsync(string sessionId, CancellationToken ct = default);

    /// <summary>列出会话全部已归档轮次（按轮次升序）。</summary>
    Task<IReadOnlyList<int>> ListRoundsAsync(string sessionId, CancellationToken ct = default);
}

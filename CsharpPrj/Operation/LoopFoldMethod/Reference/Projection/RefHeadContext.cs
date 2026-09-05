using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 投影输入：累积引用关系的中立承载（RefHead 默认投影的输入）。
/// 由编排门面/消费方从引用图（IResourceReferenceStore / IHistoryRelationManager 读侧）聚合而来，
/// 本模型不认识具体存储/HierarchyMethod 类型 —— 只描述「累积了哪些节点 + 沿时序链的先后关系」。
/// </summary>
/// <param name="ScopeKey">当前作用域锚（taskKey / historyId）。</param>
/// <param name="NodeKeys">该作用域历史上挂载的节点键集（History→Task→Node 归拢）。</param>
/// <param name="RoundSequence">round_sequence 时序边（from → to，历史顺序由引用边决定）。</param>
public sealed record RefHeadContext(
    string? ScopeKey,
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<(string From, string To)> RoundSequence);

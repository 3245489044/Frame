using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 投影输入：累积引用关系的中立承载（RefHead 默认投影的输入）。
/// 由编排门面/消费方从引用图（IResourceReferenceStore / IHistoryRelationManager 读侧）聚合而来，
/// 本模型不认识具体存储/HierarchyMethod 类型 —— 只描述「累积了哪些节点 + 其键序权威」。
/// </summary>
/// <param name="ScopeKey">当前作用域锚（taskKey / historyId）。</param>
/// <param name="NodeKeys">该作用域历史上挂载的节点键集（History→Task→Node 归拢）。</param>
/// <param name="KeyOrder">键内序数比较器（A4′ 时序权威）。缺省 <c>null</c> = <c>SessionMessageKeys.OrderComparer</c>
/// （轮次 → 轮内 seq → 键名字典序兜底）；业务节点族另有键序约定时由装配方覆写。
/// 注：<c>round_sequence</c> 引用边已退役，时序不再经图边承载。</param>
public sealed record RefHeadContext(
    string? ScopeKey,
    IReadOnlyList<string> NodeKeys,
    IComparer<string>? KeyOrder = null);

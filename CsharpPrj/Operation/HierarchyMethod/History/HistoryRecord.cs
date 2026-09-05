namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史 class 容器视图 —— History = Task 更上层的容器，归拢多个 task（每个 task 下挂多个 node）。
/// 成员（归拢的任务键集 / 挂载节点）由图引用边推导（history_owns_task / session_owns），本模型仅为查询视图，
/// 不冗余存储成员（实体真源 = ur_histories 内容 + 引用图关系）。
/// </summary>
public sealed record HistoryRecord(
    string HistoryId,
    string? Name = null,
    IReadOnlyList<string>? TaskKeys = null);

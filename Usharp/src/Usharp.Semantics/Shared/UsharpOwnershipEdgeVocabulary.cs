namespace Usharp.Semantics;

/// <summary>
/// 归属边常量**单源**（D-11）：语义层持有 4 类归属边名，闸A（<c>UsharpRelationGate</c>）与
/// 适配层对账（<c>ReferenceEdgeVocabularyCheck</c>）共用，禁止两处各写一份字面量。
/// <para>与 UR 边类型常量（<c>ReferenceEdgeTypes.*</c>）逐字一致；语义层不引 UR，故以字面量为权威面。</para>
/// </summary>
public static class UsharpOwnershipEdgeVocabulary
{
    public const string CrossDbLink = "cross_db_link";
    public const string HistoryOwnsTask = "history_owns_task";
    public const string SessionOwns = "session_owns";
    public const string TaskOwnsTask = "task_owns_task";

    /// <summary>归属族全部边（声明序）。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CrossDbLink, HistoryOwnsTask, SessionOwns, TaskOwnsTask,
    };
}
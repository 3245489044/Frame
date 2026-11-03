namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 移动请求（<c>move</c>）：把 <paramref name="TargetKey"/> 从原父改挂到新父（detach + attach 原子）。
/// </summary>
/// <param name="Operation">ensure operation id（其 remove 对由内核按同一 edge 反查）。</param>
/// <param name="TargetKey">被移动的目标键。</param>
/// <param name="FromSourceKey">原父键（必须已持有该边）。</param>
/// <param name="ToSourceKey">新父键。</param>
/// <param name="Namespace">作用域（null = 由 keyspace 解析）。</param>
/// <param name="Actor">执行者标识（记账用）。</param>
/// <param name="Origin">写入来源（决定写边角色）。</param>
public sealed record RelationMoveRequest(
    string Operation,
    string TargetKey,
    string FromSourceKey,
    string ToSourceKey,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime);

/// <summary>合并请求（<c>merge</c>）：把一批目标从原父批量改挂到新父（全成全败）。</summary>
/// <param name="Operation">ensure operation id。</param>
/// <param name="TargetKeys">被合并的目标键集（非空）。</param>
/// <param name="FromSourceKey">原父键。</param>
/// <param name="ToSourceKey">新父键。</param>
/// <param name="Namespace">作用域（null = 由 keyspace 解析）。</param>
/// <param name="Actor">执行者标识。</param>
/// <param name="Origin">写入来源。</param>
public sealed record RelationMergeRequest(
    string Operation,
    IReadOnlyList<string> TargetKeys,
    string FromSourceKey,
    string ToSourceKey,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime);

/// <summary>
/// 孤儿回收请求（<c>gc</c>）：候选目标必须显式给出（禁全库扫描），且仅 reconcile/declare 角色可用。
/// </summary>
/// <param name="Operation">ensure operation id（其 remove 对由内核反查）。</param>
/// <param name="SourceKey">边的源端点键。</param>
/// <param name="TargetKeys">候选目标键集（逐个探测存在性，缺失者删边）。</param>
/// <param name="Namespace">作用域。</param>
/// <param name="Actor">执行者标识。</param>
/// <param name="Origin">写入来源（仅 <see cref="RelationOrigin.Rebuild"/>/<see cref="RelationOrigin.Admin"/>）。</param>
public sealed record RelationGcRequest(
    string Operation,
    string SourceKey,
    IReadOnlyList<string> TargetKeys,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Rebuild);

/// <summary>
/// 维护操作端口（F3）：move / merge / gc 与 ensure/remove 同源（同一 <c>[Relation]</c> 声明、同一写原语）。
/// <para>
/// 纪律：① 失败按 SyncProtocolTriple 补偿（回到执行前逐边状态，禁留半成品）；
/// ② 不需要的写一律不落（无中间态可见）；③ 补偿结果进 <see cref="RelationResult.Diagnostics"/>（可见化，禁静默）。
/// </para>
/// </summary>
public interface IRelationMaintenancePort
{
    /// <summary>单目标改挂（detach + attach）。</summary>
    Task<RelationResult> MoveAsync(RelationMoveRequest request, CancellationToken ct = default);

    /// <summary>批量改挂（全成全败）。</summary>
    Task<RelationResult> MergeAsync(RelationMergeRequest request, CancellationToken ct = default);

    /// <summary>孤儿回收（仅对显式候选集生效）。</summary>
    Task<RelationResult> GcAsync(RelationGcRequest request, CancellationToken ct = default);
}

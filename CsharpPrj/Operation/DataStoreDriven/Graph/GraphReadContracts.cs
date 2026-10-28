using System.Collections.Generic;
using System.Threading;

namespace Operation.DataStoreDriven.Graph;

/// <summary>分页请求（offset 从 0 起；limit 由实现夹取上限）。</summary>
public sealed record GraphPageRequest(int Offset, int Limit);

/// <summary>稳定分页结果：<see cref="Total"/> = 可呈现行总数（含不可解码行），并非本页数量。</summary>
/// <param name="Items">本页行。</param>
/// <param name="Total">全部可呈现行数（解码成功 + 不可解码占位行）。</param>
/// <param name="Offset">本页起点。</param>
/// <param name="Limit">本页上限。</param>
/// <param name="Undecodable">其中不可解码行数（显式成行，禁静默丢弃；AX.NoSilentOutlet）。</param>
public sealed record GraphPage<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit, int Undecodable = 0)
{
    /// <summary>是否还有后续页。</summary>
    public bool HasMore => Offset + Items.Count < Total;
}

/// <summary>端点行（身份 = <c>_id</c>，同显示键）。</summary>
public sealed record GraphEndpointRow(
    string Key, string ResourceType, string Id, string? Symbol,
    int OutDegree, int InDegree, string? UpdatedAt);

/// <summary>边行（身份 = <c>_id</c>；一条边一个可寻址关系，禁合并为节点）。</summary>
public sealed record GraphEdgeRow(
    string Key, string SourceKey, string TargetKey, string RefType,
    double Strength, string Origin, string? UpdatedAt);

/// <summary>声明行（身份 = <c>_id</c>；不得以 RelationId 作为可删除身份）。</summary>
public sealed record GraphDeclarationRow(
    string Key, string RelationId, string WorkflowId,
    string Source, string Target, string RefType, bool Tombstoned);

/// <summary>图库分页读面（节点 / 边 / 声明三面分离；墓碑集合不出现在任一读面）。</summary>
public interface IGraphCatalogReader
{
    /// <summary>端点分页（按 <c>_id</c> 升序）。</summary>
    GraphPage<GraphEndpointRow> ReadEndpoints(int offset, int limit, CancellationToken ct = default);

    /// <summary>边分页（按 <c>_id</c> 升序）。</summary>
    GraphPage<GraphEdgeRow> ReadEdges(int offset, int limit, CancellationToken ct = default);

    /// <summary>声明分页（按 <c>_id</c> 升序）。</summary>
    GraphPage<GraphDeclarationRow> ReadDeclarations(int offset, int limit, CancellationToken ct = default);
}

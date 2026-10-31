using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 图事实只读端口的<b>委托实现</b>（DP7：本类无写成员、无存储依赖）——
/// 具体存储（SQL / 键值边库）由 Adapter 或域装配侧以只读快照注入，Core 不认识存储设施（G2）。
/// </summary>
public sealed class DelegateGraphStore : IGraphStore
{
    private readonly Func<IReadOnlyList<GraphVertex>> _vertices;
    private readonly Func<string?, IReadOnlyList<GraphEdgeRead>> _edges;
    private readonly Func<string?, IReadOnlyList<GraphDeclarationRead>> _declarations;
    private readonly Func<long> _membershipVersion;

    public DelegateGraphStore(
        Func<IReadOnlyList<GraphVertex>> vertices,
        Func<string?, IReadOnlyList<GraphEdgeRead>> edges,
        Func<string?, IReadOnlyList<GraphDeclarationRead>> declarations,
        Func<long> membershipVersion)
    {
        _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        _edges = edges ?? throw new ArgumentNullException(nameof(edges));
        _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        _membershipVersion = membershipVersion ?? throw new ArgumentNullException(nameof(membershipVersion));
    }

    /// <summary>空图（未接入任何只读快照时的零值；不伪造顶点）。</summary>
    public static DelegateGraphStore Empty { get; } = new(
        static () => [],
        static _ => [],
        static _ => [],
        static () => 0);

    public IReadOnlyList<GraphVertex> Vertices() => _vertices();

    public IReadOnlyList<GraphEdgeRead> Edges(string? kind = null) => _edges(kind);

    public IReadOnlyList<GraphDeclarationRead> Declarations(string? family = null) => _declarations(family);

    public long MembershipVersion => _membershipVersion();
}

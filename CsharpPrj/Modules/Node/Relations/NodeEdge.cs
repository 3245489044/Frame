using Modules.Node.Identity;

namespace Modules.Node.Relations;

/// <summary>边端点类别（节点 / 容器 / 工作流：三者共用「Kind + Id」形状，避免把容器键塞进节点键）。</summary>
public static class EdgeEndpointKinds
{
    /// <summary>节点端点（Id = <see cref="NodeAddress.EndpointId"/>，可含实例后缀）。</summary>
    public const string Node = ReferenceTypes.Node;

    /// <summary>容器端点（history / task / 通用容器）。</summary>
    public const string Container = "container";

    /// <summary>工作流端点。</summary>
    public const string Workflow = ReferenceTypes.Workflow;
}

/// <summary>一条边的端点（<b>实例与位置无关</b>：Id 已含实例后缀；容器/工作流用各自 Kind 表达）。</summary>
/// <param name="Kind">端点类别（<see cref="EdgeEndpointKinds"/> 或资源类型词表）。</param>
/// <param name="Id">端点 Id。</param>
public sealed record NodeEdgeEndpoint(string Kind, string Id)
{
    /// <summary>节点端点（由地址派生；实例轴随 Id 保留）。</summary>
    public static NodeEdgeEndpoint Of(NodeAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new NodeEdgeEndpoint(EdgeEndpointKinds.Node, address.EndpointId);
    }

    /// <summary>容器端点。</summary>
    public static NodeEdgeEndpoint Container(string id) => new(EdgeEndpointKinds.Container, id);

    /// <summary>工作流端点。</summary>
    public static NodeEdgeEndpoint Workflow(string id) => new(EdgeEndpointKinds.Workflow, id);

    /// <summary>是否节点端点。</summary>
    public bool IsNode => string.Equals(Kind, EdgeEndpointKinds.Node, StringComparison.Ordinal);

    /// <summary>端点文本（<c>{Kind}:{Id}</c>；唯一拼法，仅用于边键/诊断）。</summary>
    public override string ToString() => Kind + ":" + Id;
}

/// <summary>
/// 节点边（关系轴统一视图）：<b>有向</b>、端点为 <see cref="NodeEdgeEndpoint"/>、可带端口。
/// <para>
/// 与既有 <see cref="NodeRelation"/> / <c>InstanceEdge</c> 的关系 = 兼容视图（<see cref="NodeEdgeAdapters"/>）：
/// 旧类型保留，二者可双向对拍；边只承载关系，不承载顺序（N6）。
/// </para>
/// </summary>
/// <param name="From">源端点。</param>
/// <param name="To">目标端点（可空 = 悬空端，须由调用方可见化）。</param>
/// <param name="Kind">边类别（<see cref="ReferenceTypes"/> 词表）。</param>
/// <param name="FromPort">源端输出端口（数据流边）。</param>
/// <param name="ToPort">目标端输入端口（数据流边）。</param>
public sealed record NodeEdge(
    NodeEdgeEndpoint From,
    NodeEdgeEndpoint? To,
    string Kind,
    string? FromPort = null,
    string? ToPort = null)
{
    /// <summary>边键（**唯一拼法**：幂等/去重键，禁各处手拼）。</summary>
    public string EdgeKey =>
        $"{From}|{Kind}|{(To is null ? string.Empty : To.ToString())}|{FromPort}|{ToPort}";

    /// <summary>是否节点↔节点数据流边。</summary>
    public bool IsDataFlow => string.Equals(Kind, ReferenceTypes.NodeToNode, StringComparison.Ordinal);

    /// <summary>是否两端齐全（悬空端由调用方诊断，边本身不抛）。</summary>
    public bool IsComplete => To is not null;
}

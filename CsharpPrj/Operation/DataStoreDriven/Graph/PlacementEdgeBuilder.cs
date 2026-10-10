using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 位置边构建器（G2 / D3）：把 <c>placement.ushar</c> 与 <c>[Domain] placements</c> 反解出的位置关系，
/// 落为引用库**一等边**（<c>at</c>：节点定位到位置；<c>contains</c>：容器包含节点）。关系权威归属 = 引用库；
/// 节点键仅承担寻址（D5），位置/包含/归属不再编码进键。
/// </summary>
public static class PlacementEdgeBuilder
{
    /// <summary>位置定位边类型（节点 → 位置）。须在 <c>reference.ushar</c> 声明（词表门）。</summary>
    public const string At = "at";

    /// <summary>位置包含边类型（容器 → 节点）。须在 <c>reference.ushar</c> 声明（词表门）。</summary>
    public const string Contains = "contains";

    /// <summary>节点 → 位置（at）：声明节点落于某位置（位置 id 来自 placement.ushar 反解）。</summary>
    public static StaticReference BuildAt(string workflowId, string nodeKey, string placementId)
        => new(
            NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, nodeKey),
            NodeResourceTypes.Node, placementId,
            At);

    /// <summary>容器 → 节点（contains）：声明容器包含某节点。</summary>
    public static StaticReference BuildContains(string containerId, string workflowId, string nodeKey)
        => new(
            NodeResourceTypes.Node, containerId,
            NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, nodeKey),
            Contains);
}

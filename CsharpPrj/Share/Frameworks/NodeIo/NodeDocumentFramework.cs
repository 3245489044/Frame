using Modules.Node;

namespace Share.NodeIo;

/// <summary>
/// 节点文档框架组合根（按域装配一次）：把框架原语（身份/位置/载体/选择/形态）
/// 与载荷契约注册表组合成唯一读写门面。<b>无静态状态</b>（每域一实例）。
/// </summary>
public sealed class NodeDocumentFramework
{
    public NodeDocumentFramework(NodeFramework framework, NodePayloadSchemaRegistry? schemas = null)
    {
        Framework = framework ?? throw new ArgumentNullException(nameof(framework));
        Schemas = schemas ?? new NodePayloadSchemaRegistry();
        Port = new NodeDocumentPort(Framework, Schemas);
    }

    public NodeFramework Framework { get; }

    public NodePayloadSchemaRegistry Schemas { get; }

    public INodeDocumentPort Port { get; }
}

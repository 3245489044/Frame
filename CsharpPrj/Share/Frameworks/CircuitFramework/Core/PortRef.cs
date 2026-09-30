namespace Share.CircuitFramework.Core;

/// <summary>端口上的定位引用：一个输出端口 → 一个输入端口 的连接端点（Wire 的 From/To）。</summary>
public sealed class PortRef
{
    /// <summary>所属节点的唯一标识。</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>端口名（在该节点内唯一）。</summary>
    public string PortName { get; set; } = string.Empty;

    public PortRef() { }

    public PortRef(string nodeId, string portName)
    {
        NodeId = nodeId;
        PortName = portName;
    }

    public override string ToString() => $"{NodeId}.{PortName}";
}
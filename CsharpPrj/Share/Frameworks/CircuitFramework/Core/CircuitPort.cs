namespace Share.CircuitFramework.Core;

/// <summary>
/// 电路端口：具名、带类型，是数据进入/离开节点的唯一边界。
/// 节点之间不允许直接引用，只能经端口（铁律：隐式数据流必须显式连线）。
/// </summary>
public sealed class CircuitPort
{
    /// <summary>端口名，在所属节点内唯一。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>端口数据类型。CLR 类型名或语义类型标识（为可序列化而保持为字符串）。</summary>
    public string? DataType { get; set; }

    public CircuitPort() { }

    public CircuitPort(string name, string? dataType = null)
    {
        Name = name;
        DataType = dataType;
    }

    public override string ToString() => Name;
}
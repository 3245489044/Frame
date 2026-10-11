namespace Share.CircuitFramework.Core;

/// <summary>电路节点：原子计算单元。节点是黑盒，内部是 C# 方法，图只管连接与调度。</summary>
public sealed class CircuitNode
{
    /// <summary>节点唯一标识（在电路内唯一）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>节点语义类型。可为业务实现名（实现 ICircuitNode）或符号种类（Call/Branch/...）。</summary>
    public string Kind { get; set; } = "Executable";

    /// <summary>输入端口（InPort），具名、带类型。</summary>
    public List<CircuitPort> InPorts { get; } = new();

    /// <summary>输出端口（OutPort），具名、带类型。</summary>
    public List<CircuitPort> OutPorts { get; } = new();

    /// <summary>
    /// 代码实体引用（G2）：本节点对应的真实代码实体（类型/方法/签名）；null = 未绑定
    /// （投影期未提供代码引用）。绑定后「函数流动图」可与真实代码对拍，无需另立抽取世界。
    /// </summary>
    public CodeRef? Code { get; set; }

    public CircuitNode() { }

    public CircuitNode(string id, string kind = "Executable")
    {
        Id = id;
        Kind = kind;
    }

    public CircuitNode AddIn(string name, string? dataType = null)
    {
        InPorts.Add(new CircuitPort(name, dataType));
        return this;
    }

    public CircuitNode AddOut(string name, string? dataType = null)
    {
        OutPorts.Add(new CircuitPort(name, dataType));
        return this;
    }
}
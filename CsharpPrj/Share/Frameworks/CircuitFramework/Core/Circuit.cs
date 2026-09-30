namespace Share.CircuitFramework.Core;

/// <summary>电路：图容器，可被嵌套引用为另一电路的节点。无循环包含。</summary>
public sealed class Circuit
{
    /// <summary>电路唯一标识（可跨版本，供 CalleeRef 引用）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>全部节点。</summary>
    public List<CircuitNode> Nodes { get; } = new();

    /// <summary>全部边（Wire = OutPort → InPort）。</summary>
    public List<CircuitWire> Wires { get; } = new();

    /// <summary>符号集合（结构/数据流/控制流/循环递归/容错/引用）。</summary>
    public List<CircuitSymbol> Symbols { get; } = new();

    public Circuit() { }

    public Circuit(string id) => Id = id;

    /// <summary>便捷：新增节点并返回之，便于链式装配。</summary>
    public CircuitNode AddNode(string id, string kind = "Executable")
    {
        var node = new CircuitNode(id, kind);
        Nodes.Add(node);
        return node;
    }

    /// <summary>便捷：附加一个符号。</summary>
    public Circuit Append(CircuitSymbol symbol)
    {
        Symbols.Add(symbol);
        return this;
    }

    /// <summary>便捷：新增一条连线。</summary>
    public Circuit Wire(string fromNode, string fromPort, string toNode, string toPort)
    {
        Wires.Add(new CircuitWire(fromNode, fromPort, toNode, toPort));
        return this;
    }
}
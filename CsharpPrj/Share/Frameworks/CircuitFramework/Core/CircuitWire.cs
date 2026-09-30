namespace Share.CircuitFramework.Core;

/// <summary>电路边：一个 OutPort → 一个 InPort 的连接，即显式参数传递（数据流唯一通道）。</summary>
public sealed class CircuitWire
{
    /// <summary>源：输出端口。</summary>
    public PortRef From { get; set; } = new();

    /// <summary>目标：输入端口。一个端口可多扇出（fan-out），不可多扇入（fan-in，须经 Merge/Join 符号）。</summary>
    public PortRef To { get; set; } = new();

    public CircuitWire() { }

    public CircuitWire(string fromNode, string fromPort, string toNode, string toPort)
    {
        From = new PortRef(fromNode, fromPort);
        To = new PortRef(toNode, toPort);
    }

    public override string ToString() => $"{From} -> {To}";
}
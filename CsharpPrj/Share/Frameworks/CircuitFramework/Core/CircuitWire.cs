namespace Share.CircuitFramework.Core;

/// <summary>电路边：一个 OutPort → 一个 InPort 的连接，即显式参数传递（数据流唯一通道）。</summary>
public sealed class CircuitWire
{
    /// <summary>源：输出端口。</summary>
    public PortRef From { get; set; } = new();

    /// <summary>目标：输入端口。一个端口可多扇出（fan-out），不可多扇入（fan-in，须经 Merge/Join 符号）。</summary>
    public PortRef To { get; set; } = new();

    /// <summary>
    /// 受控边判定（G3，可选）：非空时本边仅在判定键为真时激活（目标节点本次不执行）。
    /// 判定载体 = 结构化数据键（端口值 / 入口值），不是表达式文本（对齐 SEP.JudgmentCarrierStructural）；
    /// null = 无条件边（现行为）。判定键格式见 <c>CircuitRuntimeIndex.ResolveJudgeKey</c>。
    /// </summary>
    public string? When { get; set; }

    public CircuitWire() { }

    public CircuitWire(string fromNode, string fromPort, string toNode, string toPort)
    {
        From = new PortRef(fromNode, fromPort);
        To = new PortRef(toNode, toPort);
    }

    public override string ToString() => $"{From} -> {To}";
}
namespace Share.Signal;

/// <summary>
/// 本轮信号终态增量（SCF SC-I4：唯一推进源）——除 <c>InFlight</c> 外均为本轮增量（读取后清零）；
/// <c>InFlight</c> 为当前在途数（不清零）。
/// <para>由 Observe 段写；投递次数 / 目标集指纹变化**不构成推进**。</para>
/// </summary>
public readonly record struct SignalDelta(
    int Terminal,
    int Completed,
    int Failed,
    int Cancelled,
    int InFlight,
    int Dispatched,
    int Duplicate)
{
    /// <summary>空增量（未观测 ⇒ 不伪造推进）。</summary>
    public static readonly SignalDelta Empty = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>是否有任意终态。</summary>
    public bool HasTerminal => Terminal > 0;

    /// <summary>是否在途。</summary>
    public bool HasInFlight => InFlight > 0;

    /// <summary>是否有推进（仅 Completed 计数）。</summary>
    public bool Progressed => Completed > 0;
}

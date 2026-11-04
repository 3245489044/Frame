namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 边产品盖章（F1 落地）：一条边写入时必须携带的结构事实。
/// <para>
/// 纪律：① 零领域词汇（流程/步骤由声明面提供，本记录只承载形态）；
/// ② 幂等键禁含易变分量（创建序号只用于追溯，不参与 <see cref="EdgeProductSpec.EdgeId"/>）；
/// ③ 纯值类型，无 IO / 无状态。
/// </para>
/// </summary>
/// <param name="Assertion">断言强度（<see cref="DependencyAssertion"/> 线上词形）。</param>
/// <param name="CreatedSeq">图内单调创建序号（与写入顺序同源）。</param>
/// <param name="OriginFlow">写入该边的流程标识（追溯键；null = 未声明）。</param>
/// <param name="OriginStep">写入该边的步骤键（追溯键；null = 未声明）。</param>
public sealed record EdgeProductStamp(
    string Assertion,
    long CreatedSeq,
    string? OriginFlow,
    string? OriginStep)
{
    /// <summary>未盖章（旧文档 / 未装配盖章源时的零值；消费方据此判「不可追溯」）。</summary>
    public static EdgeProductStamp Empty { get; } = new(string.Empty, 0, null, null);

    /// <summary>是否已盖章（断言与序号同时有值；来源流程/步骤可为未声明）。</summary>
    public bool IsStamped => !string.IsNullOrWhiteSpace(Assertion) && CreatedSeq > 0;
}

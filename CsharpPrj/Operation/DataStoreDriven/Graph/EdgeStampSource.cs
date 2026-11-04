using DataCenter.Protocol.Dataflow;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 边盖章源（F1 写侧唯一入口）：把「谁写的边」变成可追溯结构事实。
/// <para>
/// 纪律：① 零领域词汇（流程/步骤由调用方给的 origin 描述提供）；
/// ② 纯记录，不写库、不判策略；③ 序号单调（同一进程内，重启从 1 起，禁作幂等键）。
/// </para>
/// </summary>
public interface IEdgeStampSource
{
    /// <summary>取下一份盖章。</summary>
    /// <param name="refType">边类型（作为步骤键）。</param>
    /// <param name="origin">边来源（<see cref="EdgeOrigin.Declared"/> / <see cref="EdgeOrigin.Runtime"/>）。</param>
    /// <param name="originDetail">来源细节（工作流 Id / 端点位置；null = 未声明）。</param>
    EdgeProductStamp Next(string refType, string origin, string? originDetail);
}

/// <summary>缺省盖章实现：声明边 ⇒ <c>declared</c>，运行时边 ⇒ <c>observed</c>；流程 = 来源细节，步骤 = 边类型。</summary>
public sealed class DeclaredEdgeStampSource : IEdgeStampSource
{
    private long _seq;

    /// <inheritdoc />
    public EdgeProductStamp Next(string refType, string origin, string? originDetail)
    {
        var seq = Interlocked.Increment(ref _seq);
        var assertion = string.Equals(origin, EdgeOrigin.Declared, StringComparison.Ordinal)
            ? DependencyAssertions.Declared
            : DependencyAssertions.Observed;

        return new EdgeProductStamp(
            assertion,
            seq,
            string.IsNullOrWhiteSpace(originDetail) ? null : originDetail,
            string.IsNullOrWhiteSpace(refType) ? null : refType);
    }
}

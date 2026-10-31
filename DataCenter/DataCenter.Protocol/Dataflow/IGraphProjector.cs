using CSG.Unified;

namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 图投影请求 —— 与 <c>ProjectionRequest</c> 并列（不替代：状态投影与产品投影各有输入域）。
/// </summary>
/// <param name="Scope">目标分区；null = 全部分区。</param>
/// <param name="ProductIds">目标产品（null = 已登记全部产品）。</param>
public sealed record GraphProjectionRequest(
    string? Scope = null,
    IReadOnlyList<string>? ProductIds = null);

/// <summary>
/// 图投影端口（顶点 + 边<b>双通道</b>）—— 从数据产品声明与图事实派生语言无关
/// <see cref="CanonicalForm"/>；边永不进入节点集合（DP2）。
/// 纪律：只读派生、确定性输出（无时间戳）、失败不升级为状态写失败。
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §4.1 / D2。
/// </summary>
public interface IGraphProjector
{
    /// <summary>派生图（同输入两次投影结果一致）。</summary>
    CanonicalForm Project(GraphProjectionRequest request);
}

using CSG.Unified;

namespace DataCenter.Protocol.Projection;

/// <summary>
/// 投影请求 —— 投影的唯一输入值类型（图投影按需触发：轮边界/显式调用，不在写热路径重算）。
/// </summary>
/// <param name="Scope">目标分区；null = 全部分区（含根分区与全部 scope 分区）。</param>
/// <param name="Tiers">目标分层标识（注册制）；null = 已登记的全部 tier。</param>
public readonly record struct ProjectionRequest(
    string? Scope = null,
    IReadOnlyCollection<string>? Tiers = null);

/// <summary>
/// 图投影端口（DF1：图是投影，不是第二真源）—— 状态 → 语言无关 <see cref="CanonicalForm"/> 的只读派生。
/// 纪律：实现方绝不回写状态；投影失败不得升级为状态写失败（投影是派生物，可重算）。
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §7 D1。
/// </summary>
public interface IStateProjector
{
    /// <summary>从状态派生图（幂等：同状态两次投影结果一致，无隐藏状态）。</summary>
    CanonicalForm Project(ProjectionRequest request);
}

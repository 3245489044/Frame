using Modules.Node.Identity;
using Modules.Node.Instances;

namespace Modules.Node.Stream;

/// <summary>
/// Stream 聚合 → 轮产物适配（<see cref="AggregateRun"/> 是本框架产物模型的一种实现）：
/// 不新增第二套产物模型，只把聚合身份映射为容器键。
/// </summary>
public static class StreamArtifactAdapter
{
    /// <summary>把一次聚合快照映射为轮产物（子引用由调用方给出，不内联帧载荷）。</summary>
    /// <param name="aggregate">聚合快照。</param>
    /// <param name="roundKey">轮键（宿主轮次 Id 文法）。</param>
    /// <param name="children">本轮子节点地址（到达序）。</param>
    /// <param name="terminal">终答来源节点（可空）。</param>
    /// <param name="terminalText">终答文本（可空）。</param>
    public static NodeArtifact ToArtifact(
        StreamAggregate aggregate,
        string roundKey,
        IReadOnlyList<NodeAddress> children,
        NodeAddress? terminal = null,
        string? terminalText = null)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(children);

        // 缺省终答归因 = 逆到达序最后一个（与 NodeArtifactAssembler.LastProduced 同口径）。
        terminal ??= children.LastOrDefault();
        return new NodeArtifact(aggregate.IdentityKey, roundKey, children, terminal, terminalText);
    }
}

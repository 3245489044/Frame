using Modules.Node.Identity;

namespace Modules.Node.Relations;

/// <summary>
/// 边推导输入：下游地址 + 端口溯源（<c>PortKeySpace.Provenance()</c> 原样传入）+ 上游地址解析端口。
/// <para>
/// 溯源只给出上游**逻辑节点键**（裸键）；实例轴由 <see cref="ResolveUpstream"/> 补齐
/// （未装配 ⇒ 退化为 <see cref="NodeAddress.FromLegacy"/>，实例轴缺席须由调用方可见化）。
/// </para>
/// </summary>
/// <param name="Downstream">下游节点地址。</param>
/// <param name="Provenance">端口键 → 上游节点键（键集与源键集恒一致）。</param>
/// <param name="ResolveUpstream">上游逻辑键 → 地址（可空 = 退化 legacy 地址）。</param>
public sealed record EdgeDerivationInput(
    NodeAddress Downstream,
    IReadOnlyDictionary<string, string> Provenance,
    Func<string, NodeAddress?>? ResolveUpstream = null);

/// <summary>边推导端口（数据流边由入参溯源得出；实现中立，不碰载体）。</summary>
public interface IEdgeDerivation
{
    /// <summary>按溯源推导数据流边（序 = 边键序数序，稳定可比）。</summary>
    IReadOnlyList<NodeEdge> Derive(EdgeDerivationInput input);
}

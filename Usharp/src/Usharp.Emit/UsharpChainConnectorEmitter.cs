using GraphFramework.Models;
using Operation.HierarchyMethod.Nodes.Relations;
using Usharp.Semantics;
using Usharp.Syntax;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace Usharp.Emit;

/// <summary>一条落成的链间连接：上游流号 / 下游流号 / 连接边类型 / 上游链尾键 / 下游链首键（全字符串）。</summary>
public sealed record UsharpChainLink(
    string UpstreamFlowId,
    string DownstreamFlowId,
    string EdgeType,
    string TailNodeKey,
    string HeadNodeKey)
{
    public override string ToString() => $"{UpstreamFlowId}.{TailNodeKey}→{DownstreamFlowId}.{HeadNodeKey}({EdgeType})";
}

/// <summary>
/// 链间连接的发射产物：落成的连接清单 + 发射期诊断。
/// <para>只暴露字符串 / 整数（连接载体 <c>NodeConfig</c> / <c>Chain</c> 不外泄，避免测试面依赖平台闭包语义）。</para>
/// </summary>
public sealed record UsharpChainLinkResult(
    IReadOnlyList<UsharpChainLink> Links,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpDiagnosticResult
{
    /// <summary>落成的连接数（U-P9 判据取数）。</summary>
    public int LinkCount => Links.Count;

    /// <summary>被接续的「上游链尾键 → 下游链首键」对（形如 <c>tail-&gt;head</c>）。</summary>
    public IReadOnlyList<string> TailNextKeys => Links.Select(l => $"{l.TailNodeKey}->{l.HeadNodeKey}").ToList();

    /// <summary>参与连接的上游流号（去重，声明序）。</summary>
    public IReadOnlyList<string> UpstreamFlowIds => Links.Select(l => l.UpstreamFlowId).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>参与连接的下游流号（去重，声明序）。</summary>
    public IReadOnlyList<string> DownstreamFlowIds => Links.Select(l => l.DownstreamFlowId).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>连接边类型（声明序）。</summary>
    public IReadOnlyList<string> EdgeTypes => Links.Select(l => l.EdgeType).ToList();

    /// <inheritdoc />
    protected override IReadOnlyList<UsharpDiagnostic> DiagnosticList => Diagnostics;

    /// <inheritdoc />
    protected override string EmptyReport => "(no connect diagnostics)";
}

/// <summary>
/// <c>[Flow] connects=</c> → 链间连接（上游链尾 → 下游链首）。
/// <para>
/// 单一映射点：逐块经**既有** <see cref="RoundSpecChainMapper.MapToChain"/> 把 <c>RoundSpec</c> 映射为
/// <c>Chain&lt;string,NodeConfig&gt;</c>（不新增第二份 <c>RoundStep ↔ NodeConfig</c> 映射），再把上游链尾节点的
/// <c>NextNodeConfig.NextNodeKey</c> 接下游链首节点键 —— 连接能力本身是 <c>Operation\WorkFlowMethod</c> 的既有真源。
/// </para>
/// <para>一条链尾在本模型中至多接一个后继：已连接 ⇒ 保留首个并把后续 connects= **可见化**（US5005，不静默覆盖）；
/// 端点不可解析 / 边类型为空由闸A 报出，本层不产连接。</para>
/// </summary>
public static class UsharpChainConnectorEmitter
{
    /// <summary>发射本编译的全部链间连接（逐流发射一次骨架，重复引用复用同一链实例）。</summary>
    public static UsharpChainLinkResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var chains = new Dictionary<string, Chain<string, NodeConfig>?>(StringComparer.Ordinal);
        var links = new List<UsharpChainLink>();

        foreach (var flow in compilation.Flows)
            foreach (var connects in flow.Connects)
            {
                // 形状 / 端点非法由闸A（US1016/US1015）报出；此处不猜、不产连接。
                if (connects.EdgeType.Length == 0) continue;
                if (string.Equals(connects.UpstreamFlowId, flow.FlowId, StringComparison.Ordinal)) continue;

                var upstream = compilation.Flows
                    .FirstOrDefault(f => string.Equals(f.FlowId, connects.UpstreamFlowId, StringComparison.Ordinal));
                if (upstream is null) continue;

                var upstreamChain = ChainOf(compilation, upstream, chains, diagnostics);
                var downstreamChain = ChainOf(compilation, flow, chains, diagnostics);
                if (upstreamChain?.EndNode is not { } tail || downstreamChain?.StartNode is not { } head) continue;

                // 连接动作：上游链尾的下一节点键 = 下游链首键（既有 NextNodeKey 通道，不新造连接结构）。
                // E14：一条链尾至多接一个后继 —— 已连接则**可见化**并保留首个（不静默覆盖）。
                var existing = tail.nextNodeConfig.NextNodeKey;
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    if (string.Equals(existing, head.Id, StringComparison.Ordinal)) continue;   // 同一连接重复声明：幂等

                    diagnostics.Add(UsharpDiagnostic.Warn(UsharpCodes.ChainTailAlreadyLinked,
                        $"[Flow] {flow.FlowId}: 上游 '{upstream.FlowId}' 链尾 '{tail.Id}' 已连接 '{existing}'，"
                        + $"本次 connects= 指向 '{head.Id}' 被忽略（一条链尾至多接一个后继，首个连接生效）",
                        flow.SourceFile, flow.LineNumber));
                    continue;
                }

                tail.nextNodeConfig.NextNodeKey = head.Id;
                links.Add(new UsharpChainLink(upstream.FlowId, flow.FlowId, connects.EdgeType, tail.Id, head.Id));
            }

        return new UsharpChainLinkResult(links, diagnostics);
    }

    /// <summary>取流的 <c>NodeConfig</c> 链（逐流发射一次；骨架发射诊断原样汇总，不重复计入）。</summary>
    private static Chain<string, NodeConfig>? ChainOf(
        UsharpCompilation compilation,
        UsharpFlowDecl flow,
        IDictionary<string, Chain<string, NodeConfig>?> cache,
        List<UsharpDiagnostic> diagnostics)
    {
        if (cache.TryGetValue(flow.FlowId, out var cached)) return cached;

        var emitted = UsharpRoundSpecEmitter.Emit(compilation, flow.FlowId);
        diagnostics.AddRange(emitted.Diagnostics);

        var chain = emitted.Spec is { Steps.Count: > 0 } spec ? RoundSpecChainMapper.MapToChain(spec) : null;
        cache[flow.FlowId] = chain;
        return chain;
    }
}

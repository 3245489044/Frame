using DataCenter.Core.Dataflow;
using DataCenter.Protocol.Dataflow;

namespace DataCenter.Hosting;

/// <summary>
/// 图接入组件三元组（Boot 输入的一体化形态）—— 由声明源合成，宿主 **一次** 给齐；
/// 单根只 Boot 一次的语义下，禁止「先上线、后补图」（补不进去）。
/// </summary>
/// <param name="Schema">图模式声明（声明源合成；空源 = <see cref="GraphSchemaSpec.Empty"/>）。</param>
/// <param name="Store">图事实只读端口（null 语义 = <c>DelegateGraphStore.Empty</c>，不伪造）。</param>
/// <param name="Provenance">运行期 provenance（跨代复用同一实例，追溯不断链）。</param>
/// <param name="QueryDecorator">查询面装饰（读即追踪；null = 不装饰）。</param>
public sealed record GraphComponents(
    GraphSchemaSpec Schema,
    IGraphStore Store,
    IProvenanceStore Provenance,
    Func<IGraphQuery, IGraphQuery>? QueryDecorator = null);

/// <summary>
/// 声明式产品图**通用装配器**（组合根唯一入口）—— 声明源集 + 只读事实面 + 盖章端口 → <see cref="GraphComponents"/>。
/// <para>
/// 通用性纪律：本类不认识任何声明面实现（调用方按注册制给 <see cref="IDeclarationSource"/> 集），
/// 也不认识任何域；新增一个域接入 = 多传一个源，装配逻辑零改动。
/// </para>
/// <para>
/// 失败语义：合成期违规 → <see cref="DeclarativeGraphException"/>（fail-fast，不降级为空图）；
/// 空源 → 空 schema（合法零值，投影产出空图而非伪造顶点）。
/// </para>
/// </summary>
public static class GraphWiring
{
    /// <summary>装配图组件（合成 → 校验 → 追踪装饰）。</summary>
    /// <param name="sources">声明源集（空集合法 = 未接入任何域）。</param>
    /// <param name="store">图事实只读端口（null = 空图）。</param>
    /// <param name="provenance">盖章端口（null = 新建进程内实例）。</param>
    /// <param name="maxNodes">影响面 / 子图最大节点数。</param>
    /// <param name="maxDepth">最大遍历深度。</param>
    /// <param name="tapQueries">是否给查询面挂「读即追踪」装饰（缺省 true）。</param>
    public static GraphComponents Build(
        IEnumerable<IDeclarationSource> sources,
        IGraphStore? store = null,
        IProvenanceStore? provenance = null,
        int maxNodes = 10_000,
        int maxDepth = 64,
        bool tapQueries = true)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var schema = DeclarativeGraphComposer.ComposeOrThrow(sources, maxNodes, maxDepth);
        var resolvedProvenance = provenance ?? new ProvenanceStore();
        var decorator = tapQueries
            ? (Func<IGraphQuery, IGraphQuery>)(query => new ProvenanceTappedQuery(query, resolvedProvenance))
            : null;

        return new GraphComponents(
            schema,
            store ?? DelegateGraphStore.Empty,
            resolvedProvenance,
            decorator);
    }
}

namespace DataCenter.Protocol.Dataflow;

/// <summary>图事实的顶点读模型（中性：不含任何存储类型与领域词汇）。</summary>
/// <param name="Id">顶点规范键。</param>
/// <param name="Kind">顶点种类（注册制，见 <c>IStateRegistry.NodeKinds</c>）。</param>
/// <param name="Properties">附加属性（只读快照；null = 无属性）。</param>
public sealed record GraphVertex(
    string Id,
    string Kind,
    IReadOnlyDictionary<string, string?>? Properties = null);

/// <summary>
/// 图事实的边读模型 —— <b>边是关系不是顶点</b>（DP2）：独立 Id/种类/端点/断言强度，
/// 读模型不得把它渲染为顶点行。
/// </summary>
/// <param name="Id">边规范键。</param>
/// <param name="Kind">边种类（<see cref="DataEdgeKinds"/>）。</param>
/// <param name="FromId">源顶点。</param>
/// <param name="ToId">目标顶点。</param>
/// <param name="Assertion">断言强度（必填，DP4）。</param>
/// <param name="Properties">附加属性（null = 无属性）。</param>
public sealed record GraphEdgeRead(
    string Id,
    string Kind,
    string FromId,
    string ToId,
    DependencyAssertion Assertion,
    IReadOnlyDictionary<string, string?>? Properties = null);

/// <summary>关系声明（层级包含 / 执行顺序的声明面读形态）。</summary>
/// <param name="Id">声明键。</param>
/// <param name="FromId">主体（父 / 前驱）。</param>
/// <param name="ToId">客体（子 / 后继）。</param>
/// <param name="Family">边族（<see cref="DataEdgeKinds"/>）。</param>
/// <param name="Assertion">断言强度。</param>
public sealed record GraphDeclarationRead(
    string Id,
    string FromId,
    string ToId,
    string Family,
    DependencyAssertion Assertion);

/// <summary>
/// 图事实<b>只读</b>端口（DP7：图是投影，不是第二真源）—— 写仍经既有写边 owner，
/// 本接口<b>不含任何写成员</b>（写口数量 = 0，由 <c>Check-GraphSingleSource</c> 判定）。
/// 实现方：Adapter 或域装配侧（按需注入只读快照）。
/// </summary>
public interface IGraphStore
{
    /// <summary>顶点快照（确定性排序由实现方保证）。</summary>
    IReadOnlyList<GraphVertex> Vertices();

    /// <summary>边快照（<paramref name="kind"/> 非空 = 按边种类过滤）。</summary>
    IReadOnlyList<GraphEdgeRead> Edges(string? kind = null);

    /// <summary>关系声明快照（<paramref name="family"/> 非空 = 按边族过滤）。</summary>
    IReadOnlyList<GraphDeclarationRead> Declarations(string? family = null);

    /// <summary>归属边成员版本（读模型缓存失效的版本源；写归属边才推进）。</summary>
    long MembershipVersion { get; }
}

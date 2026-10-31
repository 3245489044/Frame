namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 层声明（层级轴实例）—— 与层级 schema 的层 Id 一一对应；<b>层是数据不是类型</b>，
/// 故本记录不含封闭枚举（新增层 = 注册一行）。
/// </summary>
/// <param name="LevelId">层标识（与层级 schema 的 LevelId 一致）。</param>
/// <param name="EntityType">该层实体类型名（开放词）。</param>
/// <param name="ContainsEdgeFamily">该层包含关系所用边族（null = 无包含声明）。</param>
/// <param name="ProcessId">该层处理体标识（null = 无处理声明）。</param>
public sealed record GraphLevelSpec(
    string LevelId,
    string EntityType,
    string? ContainsEdgeFamily = null,
    string? ProcessId = null);

/// <summary>
/// 边族声明（DP5）—— 每个边族显式列出允许的断言强度；族外断言即阻断。
/// </summary>
/// <param name="Family">边族名（<see cref="DataEdgeKinds"/> 的取值）。</param>
/// <param name="AllowedAssertions">该族允许的断言强度（非空）。</param>
public sealed record GraphEdgeFamilySpec(
    string Family,
    IReadOnlyList<DependencyAssertion> AllowedAssertions);

/// <summary>
/// 图模式声明（三层三轴的声明面载体）—— 层 + 边族 + 产品 + 声明边 + 处理预算。
/// <para>
/// 预算（<see cref="MaxNodes"/> / <see cref="MaxDepth"/>）为<b>强制边界</b>而非建议值：
/// 影响面闭包与子图查询到界即显式截断（禁静默返回空）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.1–§3.3、§4.1。
/// </summary>
/// <param name="Levels">层声明（层级轴）。</param>
/// <param name="EdgeFamilies">边族声明（互斥校验的取值域）。</param>
/// <param name="Products">数据产品声明（角色轴）。</param>
/// <param name="DeclaredEdges">声明面边（<see cref="DependencyAssertion.Declared"/>）。</param>
/// <param name="MaxNodes">影响面 / 子图的最大节点数（含根）。</param>
/// <param name="MaxDepth">最大遍历深度（根 = 1）。</param>
public sealed record GraphSchemaSpec(
    IReadOnlyList<GraphLevelSpec> Levels,
    IReadOnlyList<GraphEdgeFamilySpec> EdgeFamilies,
    IReadOnlyList<DataProductSpec> Products,
    IReadOnlyList<DataDependencyEdge> DeclaredEdges,
    int MaxNodes = 10_000,
    int MaxDepth = 64)
{
    /// <summary>空声明（未接入任何域时的零值；投影产出空图而非伪造节点）。</summary>
    public static GraphSchemaSpec Empty { get; } = new([], [], [], []);
}

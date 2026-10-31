namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 中性产品声明（声明源 → 图的唯一载体）—— 只承载「产品键 / 层 / 角色 / 字段契约 / 幂等键」，
/// <b>不含任何域模型与呈现词</b>（UI 文案、徽标、族名一律不进契约，由各声明源在映射时过滤）。
/// <para>
/// 纪律：本记录是 <see cref="IDeclarationSource"/> 的输出单元；新增声明面（声明式工作流 / 层级 /
/// 台账 JSON …）= 实现一个源，**框架本体零改动**（Strategy.SingleImplementation）。
/// </para>
/// </summary>
/// <param name="ProductId">产品规范键（层内唯一；形态 <c>[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*</c>）。</param>
/// <param name="LevelId">层级轴引用（与层级 schema 的层 Id 一致）。</param>
/// <param name="Role">角色轴取值（<see cref="ProductRoles"/>；注册制）。</param>
/// <param name="InFields">入字段契约（<see cref="ProductRoles.Source"/> 可空）。</param>
/// <param name="OutFields">出字段契约（对外承诺；恒非空）。</param>
/// <param name="IdempotencyKey">幂等键表达式（结构事实；禁含易变时间/序号分量，判据 C17）。</param>
/// <param name="Version">产品版本（单调不减）。</param>
/// <param name="Stage">产品阶段（<see cref="ProductStages"/>；null = 未声明）。</param>
/// <param name="Compatibility">兼容性声明（C15 取值域见 <see cref="ProductCompatibility"/>）。</param>
/// <param name="DependsOn">上游产品引用（声明面血缘）。</param>
/// <param name="Migration">迁移声明（<see cref="ProductCompatibility.Breaking"/> 时必填）。</param>
public sealed record ProductDeclaration(
    string ProductId,
    string LevelId,
    string Role,
    IReadOnlyList<FieldSpec> InFields,
    IReadOnlyList<FieldSpec> OutFields,
    string IdempotencyKey,
    int Version = 1,
    string? Stage = null,
    string? Compatibility = null,
    IReadOnlyList<string>? DependsOn = null,
    string? Migration = null);

/// <summary>中性层声明（层级轴实例）—— 层是数据不是类型（新增层 = 注册一行）。</summary>
/// <param name="LevelId">层标识。</param>
/// <param name="EntityType">该层实体类型名（开放词）。</param>
/// <param name="ContainsEdgeFamily">层包含关系所用边族（null = 无包含声明）。</param>
/// <param name="ProcessId">该层处理体标识（null = 无处理声明）。</param>
public sealed record LevelDeclaration(
    string LevelId,
    string EntityType,
    string? ContainsEdgeFamily = null,
    string? ProcessId = null);

/// <summary>
/// 中性边声明 —— 声明面**只允许** <see cref="DependencyAssertion.Declared"/>
/// （observed 由运行期盖章产生，may 由不可判定依赖显式标注；三档不得混用，DP4）。
/// </summary>
/// <param name="FromProductId">源产品。</param>
/// <param name="ToProductId">目标产品。</param>
/// <param name="EdgeKind">边种类（<see cref="DataEdgeKinds"/>）。</param>
/// <param name="FromFields">源字段路径（空 = 产品级依赖）。</param>
/// <param name="ToFields">目标字段路径（data.* 边须 ⊆ 目标产品出字段）。</param>
/// <param name="Assertion">断言强度（声明源恒 <see cref="DependencyAssertion.Declared"/>）。</param>
/// <param name="Label">
/// 边标签（同一 (端点, 边族) 上的**多重关系**区分键，如 <c>history_owns_task</c> / <c>cross_db_link</c>）。
/// null = 无标签 ⇒ 边键形态保持 <c>{from}|{kind}|{to}</c>（兼容既有声明）；非空 ⇒ <c>{from}|{kind}|{to}|{label}</c>。
/// </param>
/// <param name="Properties">边属性（声明面结构事实：trigger/stage/order/family 等；键序无语义，值须非空）。</param>
public sealed record EdgeDeclaration(
    string FromProductId,
    string ToProductId,
    string EdgeKind,
    IReadOnlyList<string> FromFields,
    IReadOnlyList<string> ToFields,
    DependencyAssertion Assertion = DependencyAssertion.Declared,
    string? Label = null,
    IReadOnlyDictionary<string, string?>? Properties = null);

/// <summary>
/// 一次声明输出的聚合（产品 + 层 + 边）—— 声明源的返回值单形体（禁三方法各自漂移）。
/// </summary>
/// <param name="Products">产品声明集。</param>
/// <param name="Levels">层声明集。</param>
/// <param name="Edges">边声明集。</param>
public sealed record DeclaredGraph(
    IReadOnlyList<ProductDeclaration> Products,
    IReadOnlyList<LevelDeclaration> Levels,
    IReadOnlyList<EdgeDeclaration> Edges)
{
    /// <summary>零声明（未接入任何声明面时的零值；合成器据此产出空图而非伪造产品）。</summary>
    public static DeclaredGraph Empty { get; } = new([], [], []);
}

/// <summary>
/// 声明源端口（**唯一扩展点**）—— 任意声明面（声明式工作流 / 层级声明 / 台账 JSON / 域目录表）
/// 只需实现本接口即可接入产品图；框架本体不认识任何具体声明面（通用性判据）。
/// <para>
/// 纪律：① 实现方只做**形态映射**（不判定图语义，不改存储）；② 呈现词（徽标/中文文案/UI 族名）
/// 不得进入 <see cref="ProductDeclaration"/>；③ 输出须确定性（同输入同输出，禁随机/时间）。
/// </para>
/// </summary>
public interface IDeclarationSource
{
    /// <summary>源标识（诊断与门禁归因；如 <c>example.ushar</c> / <c>catalog.json</c>）。</summary>
    string SourceId { get; }

    /// <summary>产出本源的声明（产品 / 层 / 边）。</summary>
    DeclaredGraph Declare();
}

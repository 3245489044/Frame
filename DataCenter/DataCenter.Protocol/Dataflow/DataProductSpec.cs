namespace DataCenter.Protocol.Dataflow;

/// <summary>角色轴取值（开放注册制：新增角色 = 增常量，不改契约）。</summary>
public static class ProductRoles
{
    /// <summary>原始产出（不可变，重算的源头）。</summary>
    public const string Source = "source";

    /// <summary>契约化转换（in → out）。</summary>
    public const string Transform = "transform";

    /// <summary>聚合 / 指标 / 视图（可随时从 raw 重算）。</summary>
    public const string Derived = "derived";

    /// <summary>边产品（关系作为一等数据产品；字段契约见 <see cref="EdgeProductSpec"/>）。</summary>
    public const string Edge = "edge";
}

/// <summary>兼容性取值（C15 的判据取值域）。</summary>
public static class ProductCompatibility
{
    /// <summary>向后兼容（只增字段）。</summary>
    public const string Backward = "backward";

    /// <summary>向前兼容（只减字段）。</summary>
    public const string Forward = "forward";

    /// <summary>双向兼容（无接口变更）。</summary>
    public const string Full = "full";

    /// <summary>破坏性变更（删字段 / 改类型；须带迁移声明）。</summary>
    public const string Breaking = "breaking";
}

/// <summary>产品阶段（对齐 Bronze/Silver/Gold，但不新增存储 tier）。</summary>
public static class ProductStages
{
    /// <summary>不可变原始产出。</summary>
    public const string Raw = "raw";

    /// <summary>契约化（字段闭合、幂等键可算）。</summary>
    public const string Normalized = "normalized";

    /// <summary>聚合 / 派生视图。</summary>
    public const string Derived = "derived";
}

/// <summary>
/// 数据产品契约（DP1：产品是契约，不是名字）—— 顶点承载的声明面。
/// 只有名字而无本记录的顶点<b>不得</b>参与 <c>data.derives</c> 边。
/// <para>
/// 纪律：零领域词汇（不出现具体业务实体名）；角色/阶段/兼容性为开放注册字符串，
/// 不为任何域另立类型体系（层是数据不是类型）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.4 / DP1。
/// </summary>
/// <param name="ProductId">规范键（层内唯一；<c>products</c> 台账的键）。</param>
/// <param name="LevelId">层级轴引用（与层级 schema 的层 Id 一致）。</param>
/// <param name="Role">角色轴取值（<see cref="ProductRoles"/>；注册制，非封闭枚举）。</param>
/// <param name="InFields">入字段契约（<see cref="ProductRoles.Source"/> 可空）。</param>
/// <param name="OutFields">出字段契约（对外承诺；恒非空）。</param>
/// <param name="IdempotencyKey">幂等键表达式（结构事实；禁含易变时间/序号分量，判据 C17）。</param>
/// <param name="Version">产品版本（单调不减）。</param>
/// <param name="Compatibility">兼容性声明（C15 取值域见 <see cref="ProductCompatibility"/>）。</param>
/// <param name="Stage">产品阶段（见 <see cref="ProductStages"/>；null = 未声明）。</param>
/// <param name="DependsOn">上游产品引用（声明面血缘；可为空）。</param>
/// <param name="Migration">迁移声明（<see cref="ProductCompatibility.Breaking"/> 时必填）。</param>
public sealed record DataProductSpec(
    string ProductId,
    string LevelId,
    string Role,
    IReadOnlyList<FieldSpec> InFields,
    IReadOnlyList<FieldSpec> OutFields,
    string IdempotencyKey,
    int Version,
    string? Compatibility = null,
    string? Stage = null,
    IReadOnlyList<string>? DependsOn = null,
    string? Migration = null);

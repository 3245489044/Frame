using Share.HierarchySupport;

namespace Strategy.Observability.Boundary;

/// <summary>
/// 边界契约（E9）：统一数据标准的<b>单一载体</b>——端口 / 数据 / 事件三类边界以同一记录表达，
/// 取代「事件契约 + 端口规格 + 产物字段 + 数据契约」四套各写一遍。
/// <para>
/// 复用 <see cref="DataScope"/> 语义不变；<see cref="ValueType"/> 仅在 C# 侧播种时给出（JSON 声明可空）。
/// </para>
/// </summary>
public sealed record BoundaryContract(
    string Key,
    BoundaryKind Kind,
    BoundaryDirection Direction,
    IReadOnlyList<FieldSpec> Fields,
    IReadOnlyList<string> MetricKeys,
    IReadOnlyList<PortDependency> Depends,
    DataScope Scope = DataScope.Round,
    Type? ValueType = null,
    int SchemaVersion = 1)
{
    /// <summary>事件边界所属实体类型（仅 <see cref="BoundaryKind.Event"/> 有意义；null = 不限）。</summary>
    public string? EntityType { get; init; }

    /// <summary>端口声明发射的事件分类（装配校验与观测面冻结依据）。</summary>
    public IReadOnlyList<string> Emits { get; init; } = [];

    /// <summary>字段名清单（声明序）。</summary>
    public IReadOnlyList<string> FieldNames => Fields.Select(f => f.Name).ToArray();

    /// <summary>依赖端口键清单（声明序）。</summary>
    public IReadOnlyList<string> DependencyKeys => Depends.Select(d => d.PortKey).ToArray();

    /// <summary>取字段契约（Ordinal 名匹配；未声明 → null）。</summary>
    public FieldSpec? Field(string name)
        => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    /// <summary>值是否可接受（null 恒可接受；声明了 ValueType 时须类型可赋值）。</summary>
    public bool Accepts(object? value)
        => value is null || ValueType is null || ValueType.IsInstanceOfType(value);

    /// <summary>端口契约（输入字段 = In，输出字段 = Out）。</summary>
    public static BoundaryContract Port(
        string key,
        IEnumerable<FieldSpec>? inputFields = null,
        IEnumerable<FieldSpec>? outputFields = null,
        IEnumerable<PortDependency>? depends = null,
        IEnumerable<string>? metricKeys = null,
        DataScope scope = DataScope.Round,
        int schemaVersion = 1,
        IEnumerable<string>? emits = null)
        => new(
            key,
            BoundaryKind.Port,
            BoundaryDirection.Both,
            (inputFields ?? []).Concat(outputFields ?? []).ToArray(),
            (metricKeys ?? []).ToArray(),
            (depends ?? []).ToArray(),
            scope,
            null,
            schemaVersion)
        {
            Emits = (emits ?? []).ToArray(),
        };

    /// <summary>数据契约（方向 + 类型 + 作用域）。</summary>
    public static BoundaryContract Data(
        string key,
        BoundaryDirection direction,
        Type? valueType = null,
        IEnumerable<FieldSpec>? fields = null,
        DataScope scope = DataScope.Round,
        int schemaVersion = 1)
        => new(
            key,
            BoundaryKind.Data,
            direction,
            (fields ?? []).ToArray(),
            [],
            [],
            scope,
            valueType,
            schemaVersion);

    /// <summary>事件契约（载荷字段 + 度量键 + 枚举域 + 可选实体类型约束）。</summary>
    public static BoundaryContract Event(
        string key,
        IEnumerable<FieldSpec>? requiredFields = null,
        IEnumerable<string>? metricKeys = null,
        string? entityType = null,
        int schemaVersion = 1)
        => new(
            key,
            BoundaryKind.Event,
            BoundaryDirection.Out,
            (requiredFields ?? []).ToArray(),
            (metricKeys ?? []).ToArray(),
            [],
            DataScope.Volatile,
            null,
            schemaVersion)
        {
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType,
        };
}

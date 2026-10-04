using DataCenter.Protocol.Standards;
using Share.HierarchySupport;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Boundary;

/// <summary>
/// 标准声明 → 边界契约的**纯投影**（M2 装载期解析退场）：端口 / 数据 / 事件三类标准模型
/// （<c>DataCenter.Protocol.Standards</c>）逐条投影为 <see cref="BoundaryContract"/>——
/// 零 JSON 解析、零文件读取；内核只接标准层已装载并校验过的声明面。
/// <para>
/// 分工（禁两处各判一遍）：键闭合与枚举合法性由标准层**结构门**（schema）承担；跨项一致性由
/// <c>C1–C13</c> 承担；本件只做形状映射。仅对「标准层归一后不应出现的值」做**防御性** fail-fast
/// （如未知 scope / direction 记号），说明标准层被绕过或版本漂移。
/// </para>
/// </summary>
public static class BoundaryContractProjection
{
    /// <summary>三本台账 → 边界契约（端口 ∪ 数据 ∪ 事件；段内保持声明序，段序 = 端口 → 数据 → 事件）。</summary>
    public static IReadOnlyList<BoundaryContract> From(
        IEnumerable<PortStandard> ports,
        IEnumerable<DataStandardItem>? data = null,
        IEnumerable<EventStandard>? events = null)
    {
        PortGuard.Required(ports, nameof(ports));

        var contracts = new List<BoundaryContract>();
        foreach (var port in ports) contracts.Add(Port(port));
        foreach (var item in data ?? []) contracts.Add(Data(item));
        foreach (var evt in events ?? []) contracts.Add(Event(evt));
        return contracts;
    }

    /// <summary>端口标准 → 端口契约（入参字段 + 产物字段 + 依赖端口 + 发射事件）。</summary>
    public static BoundaryContract Port(PortStandard port)
    {
        PortGuard.Required(port, nameof(port));

        return BoundaryContract.Port(
            port.Key,
            port.Inputs.Select(field => new FieldSpec(field.Name, Type(field.Type), field.Required)),
            port.OutFields.Select(name => new FieldSpec(name, FieldTypes.Any)),
            port.Depends.Select(key => new PortDependency(key)),
            metricKeys: [],
            scope: Scope(port.Key, port.Scope),
            schemaVersion: port.Version,
            emits: port.Emits);
    }

    /// <summary>数据标准 → 数据契约（方向 + 字段 + 作用域；字段序 = 台账声明序）。</summary>
    public static BoundaryContract Data(DataStandardItem item)
    {
        PortGuard.Required(item, nameof(item));

        return BoundaryContract.Data(
            item.Key,
            Direction(item.Key, item.Direction, BoundaryKind.Data),
            valueType: null,
            fields: item.Fields.Select(field => new FieldSpec(field.Key, Type(field.Value))),
            scope: Scope(item.Key, item.Scope),
            schemaVersion: item.Version);
    }

    /// <summary>事件标准 → 事件契约（必填载荷字段 + 度量键 + 枚举域 + 可选实体族）。</summary>
    public static BoundaryContract Event(EventStandard evt)
    {
        PortGuard.Required(evt, nameof(evt));

        // required 与 enum 同名 = 同一字段（enum 是字段属性，不是第二个字段）→ 按名并集，禁重复声明。
        var fields = FieldSpec.UnionRequiredAndEnums(evt.Required, evt.Enums);

        return BoundaryContract.Event(evt.Key, fields, evt.Metrics, evt.Entity, evt.Version);
    }

    /// <summary>类型记号：标准层未声明（空）→ 弱校验记号（与旧解析口径一致）。</summary>
    private static string Type(string? type) => string.IsNullOrWhiteSpace(type) ? FieldTypes.Any : type;

    private static DataScope Scope(string key, string? scope) => scope switch
    {
        null or "" => DataScope.Round,
        _ when scope.Equals("Root", StringComparison.OrdinalIgnoreCase) => DataScope.Root,
        _ when scope.Equals("Round", StringComparison.OrdinalIgnoreCase) => DataScope.Round,
        _ when scope.Equals("Lease", StringComparison.OrdinalIgnoreCase) => DataScope.Lease,
        _ when scope.Equals("Session", StringComparison.OrdinalIgnoreCase) => DataScope.Session,
        _ when scope.Equals("Volatile", StringComparison.OrdinalIgnoreCase) => DataScope.Volatile,
        _ => throw new BoundaryContractViolationException(key, $"未知作用域 '{scope}'（标准层结构门应已拦截）"),
    };

    private static BoundaryDirection Direction(string key, string? direction, BoundaryKind kind) => direction switch
    {
        null or "" => kind == BoundaryKind.Port ? BoundaryDirection.Both : BoundaryDirection.In,
        _ when direction.Equals("In", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.In,
        _ when direction.Equals("Out", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.Out,
        _ when direction.Equals("Both", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.Both,
        _ => throw new BoundaryContractViolationException(key, $"未知边界方向 '{direction}'（标准层结构门应已拦截）"),
    };
}

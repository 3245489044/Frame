using Strategy.Observability.Boundary;

namespace Strategy.Observability.Registry;

/// <summary>
/// 边界契约 ↔ 事件契约桥（S2 兼容层）：事件类 <see cref="BoundaryContract"/> 即
/// <see cref="EventContract"/> 的上位表达，两者语义一一对应，故 E1–E8 既有校验器零改动即可消费新契约。
/// </summary>
public static class BoundaryEventContractBridge
{
    /// <summary>边界契约（Event 类）→ 事件契约。非事件类边界抛错（不静默降级）。</summary>
    public static EventContract ToEventContract(this BoundaryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.Kind != BoundaryKind.Event)
            throw new Diagnostics.BoundaryContractViolationException(contract.Key, "只有 Event 类边界契约可投影为事件契约");

        var required = contract.Fields.Where(f => f.Required).Select(f => f.Name).ToArray();
        var enums = contract.Fields
            .Where(f => f.HasEnum)
            .ToDictionary(f => f.Name, f => f.Enum!, StringComparer.Ordinal);

        return new EventContract
        {
            EventKind = contract.Key,
            EntityType = contract.EntityType,
            Required = required,
            MetricKeys = contract.MetricKeys,
            Enums = enums,
            Version = contract.SchemaVersion,
        };
    }

    /// <summary>事件契约 → 边界契约（Event 类），供既有语义文件（observability_semantics.json）渐进迁移。</summary>
    public static BoundaryContract ToBoundaryContract(this EventContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        // required 与 enum 同名 = 同一字段（enum 是字段属性，非第二个字段）→ 按名并集；
        // 口径与标准投影（BoundaryContractProjection.Event）一致，禁重复声明（否则边界注册表按重复字段名 fail-fast）。
        var fields = new List<FieldSpec>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in contract.Required)
        {
            index[name] = fields.Count;
            fields.Add(new FieldSpec(name, FieldTypes.Any));
        }

        foreach (var (name, allowed) in contract.Enums)
        {
            if (index.TryGetValue(name, out var at))
            {
                fields[at] = fields[at] with { Enum = allowed };
                continue;
            }

            index[name] = fields.Count;
            fields.Add(new FieldSpec(name, FieldTypes.String, Required: false, Enum: allowed));
        }

        return BoundaryContract.Event(
            contract.EventKind,
            fields,
            contract.MetricKeys,
            contract.EntityType,
            contract.Version);
    }
}

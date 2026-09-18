namespace Strategy.Observability.Boundary;

/// <summary>
/// 字段契约（E9）：名 + 类型记号 + 必填 + 可选枚举域。
/// 类型用<b>记号字符串</b>而非 <see cref="Type"/>，以便契约可直接由 JSON 声明（无需程序集加载）。
/// </summary>
public sealed record FieldSpec(
    string Name,
    string Type = FieldTypes.Any,
    bool Required = true,
    IReadOnlyList<string>? Enum = null)
{
    /// <summary>该字段是否声明了枚举域。</summary>
    public bool HasEnum => Enum is { Count: > 0 };

    /// <summary>取值是否落在声明的枚举域内（未声明枚举域 = 不判）。</summary>
    public bool Allows(string? value)
        => !HasEnum || (value is not null && Enum!.Contains(value, StringComparer.Ordinal));

    /// <summary>
    /// 必填字段名 ∪ 枚举字段 → 字段契约清单（标准事件投影 <c>BoundaryContractProjection.Event</c>
    /// 与兼容桥 <c>BoundaryEventContractBridge.ToBoundaryContract</c> 共用单源）：
    /// 必填 ∩ 枚举同名 = 同一字段（枚举是字段属性，非第二个字段）→ 向必填字段并入枚举域；
    /// 未在必填集出现的枚举字段 = 可选字符串字段。段序 = 必填声明序在前、枚举新增序在后。
    /// </summary>
    public static IReadOnlyList<FieldSpec> UnionRequiredAndEnums(
        IReadOnlyList<string>? required, IReadOnlyDictionary<string, IReadOnlyList<string>>? enums)
    {
        var fields = new List<FieldSpec>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in required ?? Array.Empty<string>())
        {
            index[name] = fields.Count;
            fields.Add(new FieldSpec(name, FieldTypes.Any));
        }

        foreach (var (name, allowed) in enums
                     ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal))
        {
            if (index.TryGetValue(name, out var at))
            {
                fields[at] = fields[at] with { Enum = allowed };
                continue;
            }

            index[name] = fields.Count;
            fields.Add(new FieldSpec(name, FieldTypes.String, Required: false, Enum: allowed));
        }

        return fields;
    }
}

/// <summary>字段类型记号（E9）：契约声明用的规范化类型名。</summary>
public static class FieldTypes
{
    public const string String = "string";
    public const string Int = "int";
    public const string Number = "number";
    public const string Bool = "bool";
    public const string Object = "object";
    public const string List = "list";

    /// <summary>未声明类型（弱校验）。</summary>
    public const string Any = "any";

    /// <summary>CLR 类型 → 类型记号（未识别归 <see cref="Any"/>）。</summary>
    public static string Of(Type? type)
    {
        if (type is null) return Any;
        if (type == typeof(string) || type == typeof(char)) return String;
        if (type == typeof(bool)) return Bool;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return Int;
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return Number;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return List;
        return Object;
    }
}

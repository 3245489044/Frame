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

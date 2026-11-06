namespace Usharp.Compute;

public enum ComputeTypeKind
{
    Int,
    Decimal,
    Bool,
    String,
    Unit,
    Host,
    List,
    Map
}

/// <summary>
/// 计算层类型。没有隐式数值提升：int 与 decimal 只在同名类型上运算；
/// host 是宿主边界值（键空间值/矩阵值），只能经具名转换进入/离开计算层。
/// </summary>
public sealed record ComputeType(ComputeTypeKind Kind, ComputeType? Element = null)
{
    public static readonly ComputeType Int = new(ComputeTypeKind.Int);
    public static readonly ComputeType Decimal = new(ComputeTypeKind.Decimal);
    public static readonly ComputeType Bool = new(ComputeTypeKind.Bool);
    public static readonly ComputeType String = new(ComputeTypeKind.String);
    public static readonly ComputeType Unit = new(ComputeTypeKind.Unit);
    public static readonly ComputeType Host = new(ComputeTypeKind.Host);

    public static ComputeType ListOf(ComputeType element) => new(ComputeTypeKind.List, element);
    public static ComputeType MapOf(ComputeType value) => new(ComputeTypeKind.Map, value);

    public string Display => Kind switch
    {
        ComputeTypeKind.Int => "int",
        ComputeTypeKind.Decimal => "decimal",
        ComputeTypeKind.Bool => "bool",
        ComputeTypeKind.String => "string",
        ComputeTypeKind.Unit => "unit",
        ComputeTypeKind.Host => "host",
        ComputeTypeKind.List => $"list<{(Element ?? Unit).Display}>",
        ComputeTypeKind.Map => $"map<string,{(Element ?? Unit).Display}>",
        _ => "unknown"
    };

    public bool IsNumeric => Kind is ComputeTypeKind.Int or ComputeTypeKind.Decimal;

    public override string ToString() => Display;
}

public static class ComputeTypeParser
{
    public static bool TryParse(string? text, out ComputeType type, out string error)
    {
        type = ComputeType.Unit;
        error = "";
        var s = (text ?? "").Trim();
        if (s.Length == 0)
        {
            error = "类型为空";
            return false;
        }

        switch (s)
        {
            case "int": type = ComputeType.Int; return true;
            case "decimal": type = ComputeType.Decimal; return true;
            case "bool": type = ComputeType.Bool; return true;
            case "string": type = ComputeType.String; return true;
            case "unit": type = ComputeType.Unit; return true;
            case "host": type = ComputeType.Host; return true;
        }

        if (s.StartsWith("list<", StringComparison.Ordinal) && s.EndsWith(">", StringComparison.Ordinal))
        {
            var inner = s.Substring("list<".Length, s.Length - "list<".Length - 1);
            if (!TryParse(inner, out var element, out error))
            {
                return false;
            }

            type = ComputeType.ListOf(element);
            return true;
        }

        if (s.StartsWith("map<string,", StringComparison.Ordinal) && s.EndsWith(">", StringComparison.Ordinal))
        {
            var inner = s.Substring("map<string,".Length, s.Length - "map<string,".Length - 1);
            if (!TryParse(inner, out var element, out error))
            {
                return false;
            }

            type = ComputeType.MapOf(element);
            return true;
        }

        if (s.StartsWith("map<", StringComparison.Ordinal))
        {
            error = $"map 只允许 map<string,T>：'{s}'";
            return false;
        }

        error = $"未知类型 '{s}'（合法：int|decimal|bool|string|unit|host|list<T>|map<string,T>）";
        return false;
    }
}
